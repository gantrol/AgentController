using System.Windows;
using System.Windows.Media;
using CodexController.Models;
using CodexController.Controllers;
using Drawing = System.Windows.Media.Drawing;

namespace CodexController.Views;

/// <summary>Renders the SVG-derived layers in their original coordinate system.</summary>
public sealed class ControllerArtworkView : FrameworkElement
{
    private static readonly Lazy<ResourceDictionary> Artwork = new(() => new ResourceDictionary
    {
        Source = new Uri("/AgentController;component/Assets/ControllerArtwork.xaml", UriKind.Relative),
    });

    private static readonly (string Layer, ControllerButtons Button)[] ButtonLayers =
    [
        ("a", ControllerButtons.A), ("b", ControllerButtons.B),
        ("x", ControllerButtons.X), ("y", ControllerButtons.Y),
        ("lb", ControllerButtons.LeftShoulder), ("rb", ControllerButtons.RightShoulder),
        ("view", ControllerButtons.Back), ("menu", ControllerButtons.Start),
        ("dpad-up", ControllerButtons.DPadUp), ("dpad-down", ControllerButtons.DPadDown),
        ("dpad-left", ControllerButtons.DPadLeft), ("dpad-right", ControllerButtons.DPadRight),
    ];

    public static readonly DependencyProperty TutorialModeProperty = DependencyProperty.Register(
        nameof(TutorialMode), typeof(ControllerTutorialMode), typeof(ControllerArtworkView),
        new FrameworkPropertyMetadata(ControllerTutorialMode.Overview,
            FrameworkPropertyMetadataOptions.AffectsRender, OnTutorialModeChanged));

    private readonly Drawing _baseDrawing;
    private readonly Drawing _leftStick;
    private readonly Drawing _rightStick;
    private readonly Dictionary<string, Drawing> _pressed = new(StringComparer.Ordinal);
    private ControllerState _state = ControllerState.Disconnected;
    private double _deadZone;
    private bool _voiceActive;
    private string[] _tutorialInputs = [];
    private string? _highlightedLayer;

    public void Highlight(string? layer)
    {
        if (_highlightedLayer == layer) return;
        _highlightedLayer = layer;
        InvalidateVisual();
    }

    public Rect InputBounds(LogicalInput input)
    {
        var layer = new TutorialInput(ControllerTutorialMode.Overview, input).ArtworkLayer;
        var bounds = _pressed[layer].Bounds;
        if (layer == "left-stick") bounds.Offset(310, 591);
        if (layer == "right-stick") bounds.Offset(1191, 872);
        return bounds;
    }

    public ControllerArtworkView()
    {
        IsHitTestVisible = false;
        Focusable = false;
        _baseDrawing = LoadDrawing("Controller.Base");
        _leftStick = LoadDrawing("Controller.left-stick");
        _rightStick = LoadDrawing("Controller.right-stick");
        foreach (var name in ButtonLayers.Select(layer => layer.Layer)
                     .Concat(["lt", "rt", "left-stick", "right-stick"]))
        {
            _pressed.Add(name, LoadDrawing($"Controller.Pressed.{name}"));
        }
    }

    public ControllerTutorialMode TutorialMode
    {
        get => (ControllerTutorialMode)GetValue(TutorialModeProperty);
        set => SetValue(TutorialModeProperty, value);
    }

    public void RenderControllerState(ControllerState state, double deadZone)
    {
        var current = state.IsConnected ? state : ControllerState.Disconnected;
        var threshold = Math.Clamp(deadZone, 0, 1);
        if (_state == current && _deadZone == threshold)
        {
            return;
        }

        _state = current;
        _deadZone = threshold;
        InvalidateVisual();
    }

    public void SetVoiceHalo(bool active)
    {
        if (_voiceActive == active)
        {
            return;
        }

        _voiceActive = active;
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);
        drawingContext.DrawDrawing(_baseDrawing);
        foreach (var (layer, button) in ButtonLayers)
        {
            DrawPressed(drawingContext, layer,
                _state.Buttons.HasFlag(button) ? 1 : TutorialOpacity(layer));
        }

        DrawPressed(drawingContext, "lt", Math.Max(_voiceActive ? 1 : 0,
            Math.Max(TriggerOpacity(_state.LeftTrigger), TutorialOpacity("lt"))));
        DrawPressed(drawingContext, "rt",
            Math.Max(TriggerOpacity(_state.RightTrigger), TutorialOpacity("rt")));
        DrawStick(drawingContext, "left-stick", _leftStick, 310, 591,
            _state.LeftX, _state.LeftY, ControllerButtons.LeftThumb);
        DrawStick(drawingContext, "right-stick", _rightStick, 1191, 872,
            _state.RightX, _state.RightY, ControllerButtons.RightThumb);
    }

    private void DrawStick(DrawingContext context, string name, Drawing cap,
        double originX, double originY, double axisX, double axisY, ControllerButtons button)
    {
        var moving = Math.Max(Math.Abs(axisX), Math.Abs(axisY)) > _deadZone;
        var offsetX = moving ? Math.Clamp(axisX, -1, 1) * 36 : 0;
        var offsetY = moving ? -Math.Clamp(axisY, -1, 1) * 28 : 0;
        context.PushTransform(new TranslateTransform(originX + offsetX, originY + offsetY));
        context.DrawDrawing(cap);
        DrawPressed(context, name, _state.Buttons.HasFlag(button) ? 1
            : Math.Max(moving ? 0.28 : 0, TutorialOpacity(name)));
        context.Pop();
    }

    private void DrawPressed(DrawingContext context, string name, double opacity)
    {
        if (opacity <= 0)
        {
            return;
        }

        context.PushOpacity(opacity);
        context.DrawDrawing(_pressed[name]);
        context.Pop();
    }

    // Preview and pointer focus stay below the solid fill reserved for physical input.
    private double TutorialOpacity(string input) => _highlightedLayer == input ? 0.55
        : _tutorialInputs.Contains(input) ? 0.18 : 0;

    private static double TriggerOpacity(double value) => value > 0.03
        ? Math.Clamp(0.25 + value * 0.75, 0, 1) : 0;

    private static Drawing LoadDrawing(string key)
    {
        var drawing = (Drawing)Artwork.Value[key];
        if (drawing.CanFreeze && !drawing.IsFrozen)
        {
            drawing.Freeze();
        }

        return drawing;
    }

    private static void OnTutorialModeChanged(DependencyObject sender, DependencyPropertyChangedEventArgs e)
    {
        var view = (ControllerArtworkView)sender;
        view._tutorialInputs = (ControllerTutorialMode)e.NewValue switch
        {
            ControllerTutorialMode.Action => ["a", "b", "x", "y", "dpad-up", "dpad-down", "dpad-left", "dpad-right"],
            ControllerTutorialMode.Agent => ["lb", "view", "menu", "dpad-up", "dpad-down", "dpad-left", "dpad-right"],
            ControllerTutorialMode.Turn => ["rt", "a", "b", "x", "y"],
            ControllerTutorialMode.Command => ["rb", "view", "menu", "a", "b", "x", "y"],
            ControllerTutorialMode.StickPress => ["left-stick", "right-stick"],
            _ => [],
        };
        view.InvalidateVisual();
    }
}
