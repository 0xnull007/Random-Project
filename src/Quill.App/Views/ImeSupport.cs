using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;

namespace Quill.App.Views;

/// <summary>
/// Makes Windows input method editors (Japanese, Chinese, Korean...) work with the custom-drawn editor.
/// WPF only provides in-place composition for its own text boxes; for other elements we suspend WPF's
/// handling and attach the native IMM32 context to the window while the editor has focus, then keep the
/// composition and candidate windows positioned at the caret. Committed text still arrives through the
/// normal TextInput event. This is the approach AvalonEdit uses.
/// </summary>
internal sealed class ImeSupport : IDisposable
{
    private readonly DocumentView _view;
    private HwndSource? _hwndSource;
    private IntPtr _defaultImeWnd;
    private IntPtr _currentContext;
    private IntPtr _previousContext;
    private bool _hooked;

    public ImeSupport(DocumentView view)
    {
        _view = view;
        InputMethod.SetIsInputMethodSuspended(view, true);
        view.GotKeyboardFocus += OnGotKeyboardFocus;
        view.LostKeyboardFocus += OnLostKeyboardFocus;
    }

    public void Dispose()
    {
        _view.GotKeyboardFocus -= OnGotKeyboardFocus;
        _view.LostKeyboardFocus -= OnLostKeyboardFocus;
        ClearContext();
    }

    /// <summary>Moves the IME windows to the caret; call after the caret moves, scrolls or zooms.</summary>
    public void UpdateCompositionWindow()
    {
        if (_currentContext == IntPtr.Zero || _hwndSource is null || _hwndSource.RootVisual is null)
        {
            return;
        }

        if (_view.CaretRectInView() is not { } caretInView)
        {
            return;
        }

        try
        {
            GeneralTransform toRoot = _view.TransformToAncestor(_hwndSource.RootVisual);
            Rect caret = toRoot.TransformBounds(caretInView);
            Rect viewBounds = toRoot.TransformBounds(new Rect(_view.RenderSize));
            Matrix toDevice = _hwndSource.CompositionTarget.TransformToDevice;
            caret = Rect.Transform(caret, toDevice);
            viewBounds = Rect.Transform(viewBounds, toDevice);

            var composition = new NativeMethods.COMPOSITIONFORM
            {
                dwStyle = NativeMethods.CFS_POINT,
                ptCurrentPos = new NativeMethods.POINT((int)Math.Max(caret.Left, viewBounds.Left), (int)Math.Max(caret.Top, viewBounds.Top)),
                rcArea = NativeMethods.RECT.From(viewBounds),
            };
            NativeMethods.ImmSetCompositionWindow(_currentContext, ref composition);

            var candidate = new NativeMethods.CANDIDATEFORM
            {
                dwIndex = 0,
                dwStyle = NativeMethods.CFS_EXCLUDE,
                ptCurrentPos = new NativeMethods.POINT((int)caret.Left, (int)caret.Bottom),
                rcArea = NativeMethods.RECT.From(caret),
            };
            NativeMethods.ImmSetCandidateWindow(_currentContext, ref candidate);

            (string family, double sizeDips) = _view.CaretFont();
            double pixelHeight = sizeDips * _view.Zoom * toDevice.M22;
            NativeMethods.LOGFONT font = NativeMethods.LOGFONT.Create(family, -(int)Math.Round(pixelHeight));
            NativeMethods.ImmSetCompositionFont(_currentContext, ref font);
        }
        catch (InvalidOperationException)
        {
            // The view is not connected to a presentation source right now; try again on the next caret move.
        }
    }

    private void OnGotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (ReferenceEquals(e.NewFocus, _view))
        {
            CreateContext();
        }
    }

    private void OnLostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (ReferenceEquals(e.OldFocus, _view))
        {
            ClearContext();
        }
    }

    private void CreateContext()
    {
        ClearContext();
        _hwndSource = PresentationSource.FromVisual(_view) as HwndSource;
        if (_hwndSource is null)
        {
            return;
        }

        if (_view.IsReadOnly)
        {
            _defaultImeWnd = IntPtr.Zero;
            _currentContext = IntPtr.Zero;
        }
        else
        {
            _defaultImeWnd = NativeMethods.ImmGetDefaultIMEWnd(IntPtr.Zero);
            _currentContext = NativeMethods.ImmGetContext(_defaultImeWnd);
        }

        _previousContext = NativeMethods.ImmAssociateContext(_hwndSource.Handle, _currentContext);
        _hwndSource.AddHook(WndProc);
        _hooked = true;

        // Hand Text Services Framework focus away from WPF so the IMM32 context on our window receives the IME.
        try
        {
            NativeMethods.ITfThreadMgr? threadManager = NativeMethods.GetThreadManager();
            threadManager?.SetFocus(IntPtr.Zero);
        }
        catch (COMException)
        {
            // Without TSF the IMM32 association alone still works on most systems.
        }

        UpdateCompositionWindow();
    }

    private void ClearContext()
    {
        if (_hwndSource is null)
        {
            return;
        }

        NativeMethods.ImmAssociateContext(_hwndSource.Handle, _previousContext);
        if (_currentContext != IntPtr.Zero)
        {
            NativeMethods.ImmReleaseContext(_defaultImeWnd, _currentContext);
        }

        _currentContext = IntPtr.Zero;
        _previousContext = IntPtr.Zero;
        _defaultImeWnd = IntPtr.Zero;
        if (_hooked)
        {
            _hwndSource.RemoveHook(WndProc);
            _hooked = false;
        }

        _hwndSource = null;
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        switch (msg)
        {
            case NativeMethods.WM_INPUTLANGCHANGE:
                if (_hwndSource is not null)
                {
                    CreateContext();
                }

                break;
            case NativeMethods.WM_IME_COMPOSITION:
                UpdateCompositionWindow();
                break;
        }

        return IntPtr.Zero;
    }

    private static class NativeMethods
    {
        public const int WM_INPUTLANGCHANGE = 0x0051;
        public const int WM_IME_COMPOSITION = 0x010F;
        public const int CFS_POINT = 0x0002;
        public const int CFS_EXCLUDE = 0x0080;

#pragma warning disable SYSLIB1054 // classic P/Invoke: LOGFONT and the COM interface need runtime marshalling
        [DllImport("imm32.dll")]
        public static extern IntPtr ImmGetContext(IntPtr hWnd);

        [DllImport("imm32.dll")]
        public static extern IntPtr ImmGetDefaultIMEWnd(IntPtr hWnd);

        [DllImport("imm32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool ImmReleaseContext(IntPtr hWnd, IntPtr hIMC);

        [DllImport("imm32.dll")]
        public static extern IntPtr ImmAssociateContext(IntPtr hWnd, IntPtr hIMC);

        [DllImport("imm32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool ImmSetCompositionWindow(IntPtr hIMC, ref COMPOSITIONFORM form);

        [DllImport("imm32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool ImmSetCandidateWindow(IntPtr hIMC, ref CANDIDATEFORM form);

        [DllImport("imm32.dll", CharSet = CharSet.Unicode, EntryPoint = "ImmSetCompositionFontW")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool ImmSetCompositionFont(IntPtr hIMC, ref LOGFONT font);

        [DllImport("msctf.dll")]
        private static extern int TF_CreateThreadMgr(out ITfThreadMgr threadMgr);
#pragma warning restore SYSLIB1054

        /// <summary>The thread's existing TSF thread manager (TSF keeps one per thread), or null.</summary>
        public static ITfThreadMgr? GetThreadManager()
        {
            int hr = TF_CreateThreadMgr(out ITfThreadMgr manager);
            return hr == 0 ? manager : null;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct POINT(int x, int y)
        {
            public int X = x;
            public int Y = y;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;

            public static RECT From(Rect rect) => new()
            {
                Left = (int)Math.Floor(rect.Left),
                Top = (int)Math.Floor(rect.Top),
                Right = (int)Math.Ceiling(rect.Right),
                Bottom = (int)Math.Ceiling(rect.Bottom),
            };
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct COMPOSITIONFORM
        {
            public int dwStyle;
            public POINT ptCurrentPos;
            public RECT rcArea;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct CANDIDATEFORM
        {
            public int dwIndex;
            public int dwStyle;
            public POINT ptCurrentPos;
            public RECT rcArea;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public struct LOGFONT
        {
            public int lfHeight;
            public int lfWidth;
            public int lfEscapement;
            public int lfOrientation;
            public int lfWeight;
            public byte lfItalic;
            public byte lfUnderline;
            public byte lfStrikeOut;
            public byte lfCharSet;
            public byte lfOutPrecision;
            public byte lfClipPrecision;
            public byte lfQuality;
            public byte lfPitchAndFamily;

            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
            public string lfFaceName;

            public static LOGFONT Create(string faceName, int height) => new()
            {
                lfHeight = height,
                lfWeight = 400,
                lfCharSet = 1, // DEFAULT_CHARSET
                lfFaceName = faceName.Length > 31 ? faceName[..31] : faceName,
            };
        }

        [ComImport]
        [Guid("aa80e801-2021-11d2-93e0-0060b067b86e")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        public interface ITfThreadMgr
        {
            void Activate(out int clientId);

            void Deactivate();

            void CreateDocumentMgr(out IntPtr documentManager);

            void EnumDocumentMgrs(out IntPtr enumerator);

            void GetFocus(out IntPtr documentManager);

            void SetFocus(IntPtr documentManager);

            void AssociateFocus(IntPtr hwnd, IntPtr newDocumentManager, out IntPtr previousDocumentManager);

            void IsThreadFocus([MarshalAs(UnmanagedType.Bool)] out bool isFocus);

            int GetFunctionProvider(ref Guid clsid, out IntPtr functionProvider);

            int EnumFunctionProviders(out IntPtr enumerator);

            int GetGlobalCompartment(out IntPtr compartmentManager);
        }
    }
}
