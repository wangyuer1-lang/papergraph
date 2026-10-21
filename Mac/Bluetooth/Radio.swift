import Foundation
import CoreBluetooth
import IOBluetooth
import Network

// Transient RFCOMM service. Only forwards encrypted bytes to the app's loopback TLS
// listener; no network interface, document, pairing token or private key is exposed.
private final class Radio: NSObject, CBCentralManagerDelegate, IOBluetoothRFCOMMChannelDelegate {
    let port: UInt16
    let callback: @convention(c) (UnsafePointer<CChar>?) -> Void
    var manager: CBCentralManager?
    var service: IOBluetoothSDPServiceRecord?
    var notification: IOBluetoothUserNotification?
    var channel: IOBluetoothRFCOMMChannel?
    var tcp: NWConnection?
    var writeBuffer: NSData?
    var pending = 0
    var generation = 0
    var stopped = false
    init(_ port: UInt16, _ callback: @escaping @convention(c) (UnsafePointer<CChar>?) -> Void) {
        self.port = port; self.callback = callback; super.init()
    }
    func report(_ state: String, _ detail: String = "") {
        guard !stopped, let data = try? JSONSerialization.data(withJSONObject: ["state": state, "detail": detail]),
              let text = String(data: data, encoding: .utf8) else { return }
        text.withCString { callback($0) }
    }
    func start() {
        manager = CBCentralManager(delegate: self, queue: .main, options: [CBCentralManagerOptionShowPowerAlertKey: false])
    }
    func centralManagerDidUpdateState(_ central: CBCentralManager) {
        guard !stopped else { return }
        switch central.state {
        case .poweredOn: if service == nil { publish() }
        case .unauthorized: report("error", "Bluetooth permission was denied. Allow Papergraph in System Settings > Privacy & Security > Bluetooth."); stop()
        case .poweredOff: report("error", "Turn on Bluetooth on your Mac, then create a new code."); stop()
        case .unsupported: report("error", "Bluetooth is not available on this device."); stop()
        case .resetting: if service != nil { report("error", "Bluetooth was interrupted. Create a new code."); stop() }
        default: break
        }
    }
    func publish() {
        guard let controller = IOBluetoothHostController.default(), let address = controller.addressAsString(),
              address.replacingOccurrences(of: "-", with: "").replacingOccurrences(of: ":", with: "") != "000000000000" else {
            report("error", "Could not read the Mac Bluetooth address."); return
        }
        let uuid: [UInt8] = [0x72,0x0f,0x72,0xc6,0xa9,0x9e,0x4e,0x75,0x84,0xa4,0xe9,0x17,0x47,0x94,0x20,0x67]
        let attributes: [String: Any] = [
            "0001 - ServiceClassIDList": [Data(uuid)],
            "0004 - ProtocolDescriptorList": [[Data([0x01,0x00])], [Data([0x00,0x03]), ["DataElementType": 1, "DataElementSize": 1, "DataElementValue": 1]]],
            "0005 - BrowseGroupList": [Data([0x10,0x02])],
            "0100 - ServiceName": "Papergraph Sync",
            "LocalAttributes": ["Persistent": false]
        ]
        guard let record = IOBluetoothSDPServiceRecord.publishedServiceRecord(with: attributes) else {
            report("error", "Could not start Bluetooth sharing. Check Bluetooth permissions and try again."); return
        }
        service = record
        var channelID: BluetoothRFCOMMChannelID = 0
        guard record.getRFCOMMChannelID(&channelID) == kIOReturnSuccess, channelID > 0 else {
            report("error", "Could not start Bluetooth sharing. Check Bluetooth permissions and try again."); stop(); return
        }
        notification = IOBluetoothRFCOMMChannel.register(forChannelOpenNotifications: self,
            selector: #selector(opened(_:channel:)), withChannelID: channelID,
            direction: kIOBluetoothUserNotificationChannelDirectionIncoming)
        guard notification != nil else { report("error", "Could not listen for Bluetooth connections."); stop(); return }
        report("ready", address)
    }
    @objc func opened(_ notification: IOBluetoothUserNotification, channel incoming: IOBluetoothRFCOMMChannel) {
        guard !stopped, channel == nil else { incoming.close(); return }
        channel = incoming
        incoming.setDelegate(self)
        let link = NWConnection(host: "127.0.0.1", port: NWEndpoint.Port(rawValue: port)!, using: .tcp)
        tcp = link; let session = generation
        link.stateUpdateHandler = { [weak self] state in
            guard let self, session == self.generation, !self.stopped else { return }
            switch state {
            case .ready: self.readTCP(session)
            case .failed, .cancelled: self.closeLink()
            default: break
            }
        }
        link.start(queue: .main)
    }
    func rfcommChannelData(_ rfcommChannel: IOBluetoothRFCOMMChannel!, data pointer: UnsafeMutableRawPointer!, length: Int) {
        guard rfcommChannel === channel, let link = tcp, let pointer, length > 0 else { return }
        // Bound queued, unauthenticated traffic. TCP->radio uses one MTU in flight.
        if length > 1_048_576 - pending { closeLink(); return }
        pending += length
        let data = Data(bytes: pointer, count: length); let session = generation
        link.send(content: data, completion: .contentProcessed { [weak self] error in
            guard let self, session == self.generation else { return }
            self.pending -= length
            if error != nil { self.closeLink() }
        })
    }
    func readTCP(_ session: Int) {
        guard session == generation, let link = tcp, let channel, !stopped else { return }
        link.receive(minimumIncompleteLength: 1, maximumLength: max(1, Int(channel.getMTU()))) { [weak self] data, _, complete, error in
            guard let self, session == self.generation, let channel = self.channel else { return }
            if let data, !data.isEmpty {
                // Keep the backing bytes alive through writeComplete.
                let buffer = data as NSData; self.writeBuffer = buffer
                if channel.writeAsync(UnsafeMutableRawPointer(mutating: buffer.bytes), length: UInt16(buffer.length), refcon: nil) != kIOReturnSuccess {
                    self.closeLink()
                }
            } else if complete || error != nil { self.closeLink() }
            else { self.readTCP(session) }
        }
    }
    func rfcommChannelWriteComplete(_ rfcommChannel: IOBluetoothRFCOMMChannel!, refcon: UnsafeMutableRawPointer!, status error: IOReturn) {
        guard rfcommChannel === channel else { return }
        writeBuffer = nil
        if error != kIOReturnSuccess { closeLink() } else { readTCP(generation) }
    }
    func rfcommChannelClosed(_ rfcommChannel: IOBluetoothRFCOMMChannel!) {
        if rfcommChannel === channel { closeLink() }
    }
    func closeLink() {
        generation += 1
        let old = channel; channel = nil
        old?.setDelegate(nil); old?.close()
        tcp?.stateUpdateHandler = nil; tcp?.cancel(); tcp = nil
        writeBuffer = nil; pending = 0
    }
    func stop() {
        guard !stopped else { return }; stopped = true
        notification?.unregister(); notification = nil
        service?.remove(); service = nil
        closeLink(); manager?.delegate = nil; manager = nil
    }
}
private var radio: Radio?
@_cdecl("pg_bluetooth_start")
public func pgBluetoothStart(_ port: Int32, _ callback: @escaping @convention(c) (UnsafePointer<CChar>?) -> Void) {
    precondition(Thread.isMainThread)
    radio?.stop(); radio = nil
    guard port > 0 && port <= 65535 else { return }
    let next = Radio(UInt16(port), callback); radio = next; next.start()
}
@_cdecl("pg_bluetooth_stop")
public func pgBluetoothStop() {
    precondition(Thread.isMainThread)
    radio?.stop(); radio = nil
}

// QR contains only the same short-lived pairing code shown in the UI.
import AppKit
import CoreImage.CIFilterBuiltins
@_cdecl("pg_bluetooth_qr")
public func pgBluetoothQR(_ text: UnsafePointer<CChar>?) -> UnsafeMutablePointer<CChar>? {
    guard let text else { return nil }
    let filter = CIFilter.qrCodeGenerator()
    filter.message = Data(String(cString: text).utf8); filter.correctionLevel = "M"
    guard let output = filter.outputImage?.transformed(by: CGAffineTransform(scaleX: 6, y: 6)),
          let cg = CIContext().createCGImage(output, from: output.extent),
          let png = NSBitmapImageRep(cgImage: cg).representation(using: .png, properties: [:]) else { return nil }
    return strdup(png.base64EncodedString())
}
@_cdecl("pg_bluetooth_free")
public func pgBluetoothFree(_ pointer: UnsafeMutablePointer<CChar>?) { free(pointer) }
