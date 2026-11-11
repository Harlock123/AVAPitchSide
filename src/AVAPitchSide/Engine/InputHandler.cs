using Avalonia.Input;

namespace AVAPitchSide.Engine;

/// <summary>
/// Keyboard and two gamepads boiled down to each player's controls: the stick (up is +1, so up means up the screen)
/// and the one kick button. In a one-player game both sets of keys and both pads work for player 1.
/// </summary>
public sealed class InputHandler : IDisposable
{
    private readonly List<Key> _held = [];
    private readonly ControllerManager _pad1 = new(0), _pad2 = new(1);

    public void OnKeyDown(Key key)
    {
        _held.Remove(key);
        _held.Add(key);
    }

    public void OnKeyUp(Key key) => _held.Remove(key);
    public bool Has(Key key) => _held.Contains(key);
    private bool Any(params Key[] keys) => keys.Any(_held.Contains);

    public void Update()
    {
        _pad1.Update();
        _pad2.Update();
    }

    /// <summary>W A S D to run; Space, F or J to kick.</summary>
    public Controls KeysOne => Make(Any(Key.A), Any(Key.D), Any(Key.W), Any(Key.S), Any(Key.Space, Key.F, Key.J));

    /// <summary>The arrows to run; Right Ctrl, NumPad 0 or / to kick.</summary>
    public Controls KeysTwo => Make(Any(Key.Left), Any(Key.Right), Any(Key.Up), Any(Key.Down), Any(Key.RightCtrl, Key.NumPad0, Key.OemQuestion));

    /// <summary>A gamepad: the stick or D-pad to run, A, B or X to kick.</summary>
    public static Controls Pad(ControllerManager p) => Make(
        p.DPadLeft || p.LeftStickX < -0.45f, p.DPadRight || p.LeftStickX > 0.45f,
        p.DPadUp || p.LeftStickY < -0.45f, p.DPadDown || p.LeftStickY > 0.45f,
        p.ButtonA || p.ButtonB || p.ButtonX);

    private static Controls Make(bool left, bool right, bool up, bool down, bool kick) =>
        new((right ? 1 : 0) - (left ? 1 : 0), (up ? 1 : 0) - (down ? 1 : 0), kick);

    /// <summary>Several sets of controls together: the stick of whichever is pushed, the button if any is held.</summary>
    public static Controls Merge(params Controls[] all) => new(
        Math.Clamp(all.Sum(c => c.X), -1, 1), Math.Clamp(all.Sum(c => c.Y), -1, 1), all.Any(c => c.Kick));

    public Controls One(bool alone) => alone ? Merge(KeysOne, KeysTwo, Pad(_pad1), Pad(_pad2)) : Merge(KeysOne, Pad(_pad1));
    public Controls Two => Merge(KeysTwo, Pad(_pad2));

    /// <summary>Enter or 1.</summary>
    public bool Start => Any(Key.Enter, Key.D1) || _pad1.ButtonStart || _pad2.ButtonStart;

    public bool PadUp => _pad1.DPadUp || _pad1.LeftStickY < -0.5f;
    public bool PadDown => _pad1.DPadDown || _pad1.LeftStickY > 0.5f;
    public bool PadLeft => _pad1.DPadLeft || _pad1.LeftStickX < -0.5f;
    public bool PadRight => _pad1.DPadRight || _pad1.LeftStickX > 0.5f;
    public bool PadAccept => _pad1.ButtonA || _pad1.ButtonB;
    public bool PadDone => _pad1.ButtonStart;
    public bool PadPause => _pad1.ButtonStart || _pad2.ButtonStart;
    public bool PadBack => _pad1.ButtonBack || _pad2.ButtonBack;

    public void Dispose()
    {
        _pad1.Dispose();
        _pad2.Dispose();
    }
}
