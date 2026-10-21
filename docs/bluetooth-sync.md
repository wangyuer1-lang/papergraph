> This desktop-only public release does not include an Android app or Android source. The Mac Bluetooth adapter below remains experimental and requires a separately distributed compatible Android app; use LAN sync for the two desktop apps.

# Bluetooth sync preview: Android and Mac

This preview adds a direct, offline Bluetooth transport for Android phones and Mac computers. It requires no shared Wi-Fi, account, or server. Windows retains LAN sync; this release does not add Windows Bluetooth support.

## Try it

1. This feature requires a separately distributed compatible Android preview; it is not included here. Update the Mac app to 0.16.0-preview.8.
2. Turn on Bluetooth on both devices and keep them nearby. On Mac choose **File → Device sync → Create Bluetooth code (Android)**. Allow Bluetooth access if the system asks.
3. Use the phone's system QR scanner to read the Mac QR code, copy its text, and paste into Papergraph's **Sync → Bluetooth · Connect to Mac**. Alternatively transfer the entire `BT1-` code manually. The app does not yet include its own camera scanner.
4. Allow Android **Nearby devices** access. Confirm matching numeric pairing prompts on both systems if asked. Initial pairing can take up to 90 seconds. If necessary, pair the devices in their system Bluetooth settings first, then create a fresh Papergraph code.
5. Review changes on the phone, choose **Sync**, then **Accept and sync** on Mac. Keep both apps open until both report completion.

Codes expire after ten minutes and are single-use following authentication. Closing the Mac window or disconnecting stops its transient Bluetooth service. The phone disconnects when backgrounded; system permission/pairing dialogs are handled separately.

Both transports use the existing shared-history merge, explicit confirmation, atomic writes, freshness checks, and local backups. Same-field conflicts default to keeping both complete graphs. Entire-graph deletions are not propagated; separately imported first-time copies remain separate. Preferences and undo history are not synchronized.

## Implementation and validation

- Android uses secure RFCOMM and a fixed Papergraph service UUID, with the target Bluetooth address from the code. No scanning, location, cloud, or background-service permission is requested.
- Mac uses a transient IOBluetooth SDP/RFCOMM service in a bundled native library. Core Bluetooth requests/checks authorization. Encrypted bytes are forwarded only to a loopback TLS listener in the same app; no Wi-Fi interface is opened for Bluetooth mode.
- The code carries a random 128-bit token and the full SHA-256 certificate fingerprint. TLS 1.2/1.3, single-use authentication and both-end confirmation remain required.
- Native reads/writes are bounded and chunked to the radio MTU. Disconnect and timeout release the socket, native service and pending approval.
- Shared protocol tests cover fragmenting a duplex stream, Unicode preservation, bad certificate pins/tokens, confirmation gating, merge conflicts and storage recovery. This simulated stream coverage does not validate physical radio interoperability.
- Real Mac adapter: service creation, shutdown/restart and QR generation verified. Android signed in-place update and app launch verified in an emulator. Three-language UI and narrow layouts verified.
- **A physical Android-to-Mac Bluetooth transfer still needs testing on the user's phone.** Emulators cannot prove physical radio connectivity.

## Build

The Mac project compiles `Mac/Bluetooth/Radio.swift` with Xcode Command Line Tools and copies `libPapergraphBluetooth.dylib` into build/publish outputs. The package includes `NSBluetoothAlwaysUsageDescription`. If the active SDK is newer than the installed Swift compiler, set `MACOS_BLUETOOTH_SDK` to a compatible installed SDK. The Android build is not part of this repository.

Protocol tests: `papergraph --lan-sync-test --data-dir <fresh-test-directory>`.
Mac native UI smoke test: `papergraph --bluetooth-ui-test --data-dir <fresh-test-directory>`.
