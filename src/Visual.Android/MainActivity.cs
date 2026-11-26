using System.Runtime.InteropServices;
using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.Graphics;
using Android.Opengl;
using Android.OS;
using Android.Views;
using Android.Widget;
using QRCoder;
using Visual.Core;
using Color = Android.Graphics.Color;
using View = Android.Views.View;

namespace VisualApp;

/// <summary>
/// Full-screen visualizer for phones and Google TV. There is no editing UI on the TV:
/// everything is edited from the Wi-Fi remote page (phone or PC browser), whose QR is shown on screen.
/// </summary>
// SingleTask: a second instance couldn't open the remote's port, and the paused first one would keep it without answering
[Activity(Label = "Visual", MainLauncher = true, Exported = true, LaunchMode = LaunchMode.SingleTask,
    Theme = "@android:style/Theme.Black.NoTitleBar.Fullscreen",
    ScreenOrientation = ScreenOrientation.SensorLandscape,
    ConfigurationChanges = ConfigChanges.Orientation | ConfigChanges.ScreenSize | ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize
                           | ConfigChanges.Keyboard | ConfigChanges.KeyboardHidden | ConfigChanges.Navigation | ConfigChanges.UiMode)]
[IntentFilter([Intent.ActionMain], Categories = ["android.intent.category.LEANBACK_LAUNCHER"])]
public class MainActivity : Activity
{
    const string RecordAudio = "android.permission.RECORD_AUDIO";
    GLSurfaceView view = null!;
    Engine engine = null!;
    LinearLayout overlay = null!;
    TextView status = null!;
    Timer? tick;

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        Window!.AddFlags(WindowManagerFlags.KeepScreenOn);
        // reachable over USB / file managers: Android/data/com.visual.isf/files
        var files = GetExternalFilesDir(null)?.AbsolutePath ?? FilesDir!.AbsolutePath;
        CopyDefaultShaders(System.IO.Path.Combine(files, "shaders"));
        engine = new Engine(files, this);

        view = new GLSurfaceView(this);
        view.SetEGLContextClientVersion(3);
        view.PreserveEGLContextOnPause = true;
        view.SetRenderer(engine);

        var root = new FrameLayout(this);
        root.AddView(view);
        root.AddView(BuildOverlay(), new FrameLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.MatchParent));
        status = new TextView(this) { TextSize = 14 };
        status.SetTextColor(Color.Rgb(255, 120, 120));
        status.SetPadding(Dp(16), Dp(12), Dp(16), 0);
        root.AddView(status, new FrameLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent));
        SetContentView(root);
        HideSystemBars();

        if (CheckSelfPermission(RecordAudio) == Permission.Granted) engine.StartAudio();
        else RequestPermissions([RecordAudio], 1);

        tick = new Timer(_ => RunOnUiThread(() => status.Text = engine.Status), null, 500, 500);
    }

    View BuildOverlay()
    {
        overlay = new LinearLayout(this) { Orientation = Orientation.Vertical };
        overlay.SetGravity(GravityFlags.Center);
        overlay.SetBackgroundColor(Color.Argb(200, 0, 0, 0));
        var title = new TextView(this) { Text = "Control remoto", TextSize = 24, Gravity = GravityFlags.Center };
        title.SetTextColor(Color.White);
        var hint = new TextView(this)
        {
            Text = "Escaneá con el teléfono (misma red Wi-Fi) para editar todo.\nOK: mostrar/ocultar esto · ▶: siguiente escena",
            TextSize = 16, Gravity = GravityFlags.Center,
        };
        hint.SetTextColor(Color.LightGray);
        var url = new TextView(this) { Text = engine.Url, TextSize = 16, Gravity = GravityFlags.Center };
        url.SetTextColor(Color.White);
        overlay.AddView(title);
        overlay.AddView(new QrView(this, engine.Url), new LinearLayout.LayoutParams(Dp(260), Dp(260)) { TopMargin = Dp(16), BottomMargin = Dp(16) });
        overlay.AddView(url);
        overlay.AddView(hint);
        if (engine.RemoteError != null)
        {
            var err = new TextView(this) { Text = "No se pudo abrir el control remoto: " + engine.RemoteError, TextSize = 16 };
            err.SetTextColor(Color.Rgb(255, 120, 120));
            overlay.AddView(err);
        }
        return overlay;
    }

    void ToggleOverlay() => overlay.Visibility = overlay.Visibility == ViewStates.Visible ? ViewStates.Gone : ViewStates.Visible;

    public override bool OnKeyDown(Keycode keyCode, KeyEvent? e)
    {
        switch (keyCode)
        {
            case Keycode.DpadRight or Keycode.MediaNext or Keycode.ChannelUp:
                engine.Pending.Enqueue(() => engine.Player.Next());
                return true;
            case Keycode.DpadCenter or Keycode.Enter or Keycode.NumpadEnter or Keycode.ButtonA or Keycode.Menu:
                ToggleOverlay();
                return true;
            case Keycode.Back when overlay.Visibility == ViewStates.Visible:
                ToggleOverlay();
                return true;
        }
        return base.OnKeyDown(keyCode, e);
    }

    public override bool OnTouchEvent(MotionEvent? e)
    {
        if (e?.Action == MotionEventActions.Up) ToggleOverlay();
        return true;
    }

    public override void OnRequestPermissionsResult(int requestCode, string[] permissions, Permission[] grantResults)
    {
        base.OnRequestPermissionsResult(requestCode, permissions, grantResults);
        if (grantResults.Length > 0 && grantResults[0] == Permission.Granted) engine.StartAudio();
        else engine.AudioError = "Sin permiso de audio: las animaciones no siguen la música.";
    }

    protected override void OnPause()
    {
        view.OnPause(); // waits for the GL thread to stop, so saving here doesn't race a frame
        engine.Save();
        if (IsFinishing) engine.Stop(); // free the port and the audio now: OnDestroy can run after the next OnCreate
        base.OnPause();
    }

    protected override void OnResume()
    {
        base.OnResume();
        HideSystemBars();
        view.OnResume();
    }

    protected override void OnDestroy()
    {
        tick?.Dispose();
        engine.Stop();
        base.OnDestroy();
    }

#pragma warning disable CA1422 // the replacement (WindowInsetsController) is API 30+; this still works there
    void HideSystemBars() => Window!.DecorView.SystemUiFlags = (SystemUiFlags.Fullscreen
        | SystemUiFlags.HideNavigation | SystemUiFlags.ImmersiveSticky | SystemUiFlags.LayoutFullscreen | SystemUiFlags.LayoutHideNavigation);
#pragma warning restore CA1422

    int Dp(int dp) => (int)(dp * Resources!.DisplayMetrics!.Density);

    /// <summary>Bundled shaders go to the shader folder once; files the user changed or uploaded are kept.</summary>
    void CopyDefaultShaders(string folder)
    {
        Directory.CreateDirectory(folder);
        foreach (var name in Assets!.List("shaders") ?? [])
        {
            var dest = System.IO.Path.Combine(folder, name);
            if (File.Exists(dest)) continue;
            using var src = Assets.Open("shaders/" + name);
            using var dst = File.Create(dest);
            src.CopyTo(dst);
        }
    }

    sealed class QrView : View
    {
        readonly List<System.Collections.BitArray> m;
        readonly Paint black = new() { Color = Color.Black }, white = new() { Color = Color.White };

        public QrView(Context c, string text) : base(c)
        {
            using var gen = new QRCodeGenerator();
            using var data = gen.CreateQrCode(text, QRCodeGenerator.ECCLevel.M);
            m = data.ModuleMatrix; // includes the white quiet zone
        }

        protected override void OnDraw(Canvas canvas)
        {
            int n = m.Count;
            float cell = MathF.Floor(Math.Min(Width, Height) / (float)n);
            canvas.DrawRect(0, 0, n * cell, n * cell, white);
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                    if (m[y][x]) canvas.DrawRect(x * cell, y * cell, (x + 1) * cell, (y + 1) * cell, black);
        }
    }
}

/// <summary>GL ES entry points for Silk.NET: core functions straight from libGLESv3, extensions via EGL.</summary>
static class GlLoader
{
    [DllImport("libEGL.so")] static extern nint eglGetProcAddress(string name);
    static readonly nint lib = NativeLibrary.Load("libGLESv3.so");
    public static nint Get(string name) => NativeLibrary.TryGetExport(lib, name, out var p) ? p : eglGetProcAddress(name);
}
