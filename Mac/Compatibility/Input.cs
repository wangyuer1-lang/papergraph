using A=Avalonia.Input;
namespace System.Windows.Input
{
    [Flags] public enum ModifierKeys{None=0,Control=1,Shift=2,Alt=4}
    public enum MouseButton{Left,Middle,Right}
    public static class Keyboard{[ThreadStatic] public static ModifierKeys Modifiers;}
    public static class InputMethod{public static void SetIsInputMethodEnabled(object target,bool value){}}
    public static class Cursors
    {
        public static A.Cursor Arrow{get;}=new(A.StandardCursorType.Arrow);
        public static A.Cursor SizeAll{get;}=new(A.StandardCursorType.SizeAll);
        public static A.Cursor ScrollAll=>SizeAll;
        public static A.Cursor SizeNWSE{get;}=new(A.StandardCursorType.BottomRightCorner);
        public static A.Cursor Cross{get;}=new(A.StandardCursorType.Cross);
        public static A.Cursor Hand{get;}=new(A.StandardCursorType.Hand);
    }
    public class MouseEventArgs(A.PointerEventArgs args)
    {
        public bool Handled{get=>args.Handled;set=>args.Handled=value;}
        public Point GetPosition(Avalonia.Visual target)=>args.GetPosition(target);
    }
    public sealed class MouseButtonEventArgs(A.PointerEventArgs args,MouseButton button,int clicks=1):MouseEventArgs(args)
    {public MouseButton ChangedButton=>button;public int ClickCount=>clicks;}
    public sealed class MouseWheelEventArgs(A.PointerWheelEventArgs args):MouseEventArgs(args){public double Delta=>args.Delta.Y*120;}
}
namespace System.Windows
{
    // Adapter only for the existing custom graph surface; the surrounding UI is native Avalonia.
    public class FrameworkElement:Avalonia.Controls.Control
    {
        Avalonia.Input.IPointer? pointer;
        public double ActualWidth=>base.Bounds.Width;public double ActualHeight=>base.Bounds.Height;
        public object? FocusVisualStyle { get=>FocusAdorner; set=>FocusAdorner=null; }
        bool controlClick;
        public bool IsMouseCaptured=>pointer?.Captured==this;
        public void CaptureMouse()=>pointer?.Capture(this);
        public void ReleaseMouseCapture()=>pointer?.Capture(null);
        public override void Render(Avalonia.Media.DrawingContext context){using var drawing=new Media.DrawingContext(context);OnRender(drawing);}
        protected virtual void OnRender(Media.DrawingContext dc){}
        void Prepare(Avalonia.Input.PointerEventArgs e)
        {
            pointer=e.Pointer;
            Input.Keyboard.Modifiers=Input.ModifierKeys.None;
            if((e.KeyModifiers&(Avalonia.Input.KeyModifiers.Control|Avalonia.Input.KeyModifiers.Meta))!=0)Input.Keyboard.Modifiers|=Input.ModifierKeys.Control;
            if(e.KeyModifiers.HasFlag(Avalonia.Input.KeyModifiers.Shift))Input.Keyboard.Modifiers|=Input.ModifierKeys.Shift;
            if(controlClick)Input.Keyboard.Modifiers&=~Input.ModifierKeys.Control;
            if(e.KeyModifiers.HasFlag(Avalonia.Input.KeyModifiers.Alt))Input.Keyboard.Modifiers|=Input.ModifierKeys.Alt;
        }
        protected override void OnPointerPressed(Avalonia.Input.PointerPressedEventArgs e){var p=e.GetCurrentPoint(this).Properties;controlClick=OperatingSystem.IsMacOS()&&p.IsLeftButtonPressed&&e.KeyModifiers.HasFlag(Avalonia.Input.KeyModifiers.Control);Prepare(e);OnMouseDown(new(e,controlClick||p.IsRightButtonPressed?Input.MouseButton.Right:p.IsMiddleButtonPressed?Input.MouseButton.Middle:Input.MouseButton.Left,e.ClickCount));}
        protected override void OnPointerMoved(Avalonia.Input.PointerEventArgs e){Prepare(e);OnMouseMove(new(e));}
        protected override void OnPointerReleased(Avalonia.Input.PointerReleasedEventArgs e){Prepare(e);OnMouseUp(new(e,controlClick?Input.MouseButton.Right:e.InitialPressMouseButton switch{Avalonia.Input.MouseButton.Right=>Input.MouseButton.Right,Avalonia.Input.MouseButton.Middle=>Input.MouseButton.Middle,_=>Input.MouseButton.Left}));controlClick=false;}
        protected override void OnPointerExited(Avalonia.Input.PointerEventArgs e){Prepare(e);OnMouseLeave(new(e));}
        protected override void OnPointerWheelChanged(Avalonia.Input.PointerWheelEventArgs e){Prepare(e);OnMouseWheel(new(e));}
        protected override void OnPointerCaptureLost(Avalonia.Input.PointerCaptureLostEventArgs e){OnLostMouseCapture(null!);}
        protected virtual void OnMouseDown(Input.MouseButtonEventArgs e){}protected virtual void OnMouseUp(Input.MouseButtonEventArgs e){}
        protected virtual void OnMouseMove(Input.MouseEventArgs e){}protected virtual void OnMouseLeave(Input.MouseEventArgs e){}
        protected virtual void OnLostMouseCapture(Input.MouseEventArgs e){}protected virtual void OnMouseWheel(Input.MouseWheelEventArgs e){}
    }
}
namespace System.Windows.Threading
{
    public enum DispatcherPriority{Background}
    public sealed class Dispatcher
    {
        public Task<object> InvokeAsync(Func<object> action,DispatcherPriority priority,CancellationToken cancellationToken)
        {
            var result=new TaskCompletionSource<object>(TaskCreationOptions.RunContinuationsAsynchronously);
            Avalonia.Threading.Dispatcher.UIThread.Post(()=>{if(cancellationToken.IsCancellationRequested){result.TrySetCanceled(cancellationToken);return;}try{result.TrySetResult(action());}catch(Exception ex){result.TrySetException(ex);}},Avalonia.Threading.DispatcherPriority.Background);
            return result.Task;
        }
    }
}
