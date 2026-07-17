using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using CodexController.Controllers;
using CodexController.Models;
using CodexController.Presentation;
using CodexController.ViewModels;
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;
using Path = System.Windows.Shapes.Path;
using Point = System.Windows.Point;
using Size = System.Windows.Size;
using UserControl = System.Windows.Controls.UserControl;

namespace CodexController.Views;

public partial class RadialMenuView : UserControl
{
    private const double DialCenterX = 490;
    private const double DialCenterY = 310;
    private const double DialOuterRadius = 236;
    private const double DialInnerRadius = 144;
    private const double SourceAnchorRadius = 148;

    private RadialMenuViewModel? _subscribedViewModel;
    private bool _leaderRefreshQueued;

    public RadialMenuView()
    {
        InitializeComponent();
        DataContextChanged += RadialMenuView_DataContextChanged;
    }

    private void RadialMenuView_Loaded(
        object sender,
        RoutedEventArgs e)
    {
        BuildSectorGeometry();
        PositionSourceAnchors();
        SubscribeToViewModel(DataContext as RadialMenuViewModel);
        QueueLeaderRefresh();
    }

    private void RadialMenuView_Unloaded(
        object sender,
        RoutedEventArgs e)
    {
        SubscribeToViewModel(null);
        LeaderCanvas.Children.Clear();
        HideAllTargetAnchors();
    }

    private void RadialMenuView_SizeChanged(
        object sender,
        SizeChangedEventArgs e)
    {
        QueueLeaderRefresh();
    }

    private void RadialMenuView_DataContextChanged(
        object sender,
        DependencyPropertyChangedEventArgs e)
    {
        SubscribeToViewModel(e.NewValue as RadialMenuViewModel);
        QueueLeaderRefresh();
    }

    private void SubscribeToViewModel(RadialMenuViewModel? viewModel)
    {
        if (ReferenceEquals(_subscribedViewModel, viewModel))
        {
            return;
        }

        if (_subscribedViewModel is not null)
        {
            _subscribedViewModel.PropertyChanged -= ViewModel_PropertyChanged;
            foreach (var slot in Slots(_subscribedViewModel))
            {
                slot.PropertyChanged -= ViewModel_PropertyChanged;
            }
        }

        _subscribedViewModel = viewModel;
        if (_subscribedViewModel is null)
        {
            return;
        }

        _subscribedViewModel.PropertyChanged += ViewModel_PropertyChanged;
        foreach (var slot in Slots(_subscribedViewModel))
        {
            slot.PropertyChanged += ViewModel_PropertyChanged;
        }
    }

    private void ViewModel_PropertyChanged(
        object? sender,
        PropertyChangedEventArgs e)
    {
        QueueLeaderRefresh();
    }

    private void BuildSectorGeometry()
    {
        TopSector.Data = CreateAnnularSector(-119, -61);
        RightSector.Data = CreateAnnularSector(-59, -1);
        CenterRightSector.Data = CreateAnnularSector(1, 59);
        BottomSector.Data = CreateAnnularSector(61, 119);
        CenterLeftSector.Data = CreateAnnularSector(121, 179);
        LeftSector.Data = CreateAnnularSector(181, 239);
    }

    private static Geometry CreateAnnularSector(
        double startAngle,
        double endAngle)
    {
        var outerStart = PointOnCircle(
            startAngle,
            DialOuterRadius);
        var outerEnd = PointOnCircle(
            endAngle,
            DialOuterRadius);
        var innerEnd = PointOnCircle(
            endAngle,
            DialInnerRadius);
        var innerStart = PointOnCircle(
            startAngle,
            DialInnerRadius);

        var figure = new PathFigure
        {
            StartPoint = outerStart,
            IsClosed = true,
            IsFilled = true,
        };
        figure.Segments.Add(new ArcSegment(
            outerEnd,
            new Size(DialOuterRadius, DialOuterRadius),
            0,
            false,
            SweepDirection.Clockwise,
            true));
        figure.Segments.Add(new LineSegment(innerEnd, true));
        figure.Segments.Add(new ArcSegment(
            innerStart,
            new Size(DialInnerRadius, DialInnerRadius),
            0,
            false,
            SweepDirection.Counterclockwise,
            true));

        return new PathGeometry([figure]);
    }

    private void PositionSourceAnchors()
    {
        PositionSourceAnchor(TopSourceAnchor, -90);
        PositionSourceAnchor(RightSourceAnchor, -30);
        PositionSourceAnchor(CenterRightSourceAnchor, 30);
        PositionSourceAnchor(BottomSourceAnchor, 90);
        PositionSourceAnchor(CenterLeftSourceAnchor, 150);
        PositionSourceAnchor(LeftSourceAnchor, 210);
    }

    private static void PositionSourceAnchor(
        FrameworkElement anchor,
        double angle)
    {
        var point = PointOnCircle(angle, SourceAnchorRadius);
        Canvas.SetLeft(anchor, point.X - 5);
        Canvas.SetTop(anchor, point.Y - 5);
    }

    private static Point PointOnCircle(
        double angle,
        double radius)
    {
        var radians = angle * Math.PI / 180;
        return new Point(
            DialCenterX + Math.Cos(radians) * radius,
            DialCenterY + Math.Sin(radians) * radius);
    }

    private void QueueLeaderRefresh()
    {
        if (!IsLoaded || _leaderRefreshQueued)
        {
            return;
        }

        _leaderRefreshQueued = true;
        _ = Dispatcher.BeginInvoke(
            DispatcherPriority.Loaded,
            () =>
            {
                _leaderRefreshQueued = false;
                RefreshLeaderLines();
            });
    }

    private void RefreshLeaderLines()
    {
        LeaderCanvas.Children.Clear();
        HideAllTargetAnchors();

        if (
            _subscribedViewModel is not { IsVisible: true } viewModel ||
            viewModel.IsAgentLayer)
        {
            return;
        }

        var targetAnchors = TargetAnchors();
        foreach (var (slot, sourceAnchor) in SlotSources(viewModel))
        {
            if (
                !slot.IsPresent ||
                slot.LogicalInput is not { } logicalInput ||
                !RadialMenuControlAnchorMap.TryResolve(
                    logicalInput,
                    out var targetKey) ||
                !targetAnchors.TryGetValue(
                    targetKey,
                    out var targetAnchor))
            {
                continue;
            }

            var source = ElementCenter(sourceAnchor, LeaderCanvas);
            var target = ElementCenter(targetAnchor, LeaderCanvas);
            if (!IsFinite(source) || !IsFinite(target))
            {
                continue;
            }

            targetAnchor.Opacity = slot.IsActionEnabled ? 1 : 0.38;
            var geometry = CreateLeaderGeometry(
                source,
                target,
                logicalInput);
            AddLeaderPath(
                geometry,
                slot.IsActionEnabled,
                slot.IsHighlighted);
        }
    }

    private void AddLeaderPath(
        Geometry geometry,
        bool isEnabled,
        bool isHighlighted)
    {
        var accent =
            TryFindResource("RadialDial.LineBrush") as Brush ??
            Brushes.Teal;
        LeaderCanvas.Children.Add(new Path
        {
            Data = geometry,
            Stroke = accent,
            StrokeThickness = isHighlighted ? 7 : 5,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            Opacity = isEnabled
                ? isHighlighted ? 0.2 : 0.1
                : 0.05,
            IsHitTestVisible = false,
        });
        LeaderCanvas.Children.Add(new Path
        {
            Data = geometry,
            Stroke = accent,
            StrokeThickness = isHighlighted ? 2.8 : 2,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            Opacity = isEnabled ? 0.94 : 0.34,
            IsHitTestVisible = false,
        });
    }

    private static Geometry CreateLeaderGeometry(
        Point source,
        Point target,
        LogicalInput input)
    {
        var (control1, control2) = input switch
        {
            LogicalInput.DPadUp =>
                (new Point(source.X, source.Y + 44),
                 new Point(target.X, target.Y - 34)),
            LogicalInput.DPadDown =>
                (new Point(source.X, source.Y - 44),
                 new Point(target.X, target.Y + 34)),
            LogicalInput.DPadLeft =>
                (new Point(source.X + 42, source.Y),
                 new Point(target.X - 34, target.Y)),
            LogicalInput.DPadRight =>
                (new Point(source.X - 50, source.Y),
                 new Point(target.X + 42, target.Y)),
            LogicalInput.View =>
                (new Point(source.X + 42, source.Y),
                 new Point(target.X - 32, target.Y)),
            LogicalInput.Menu =>
                (new Point(source.X - 42, source.Y),
                 new Point(target.X + 32, target.Y)),
            _ => DefaultControlPoints(source, target),
        };

        var figure = new PathFigure
        {
            StartPoint = source,
            IsClosed = false,
            IsFilled = false,
        };
        figure.Segments.Add(new BezierSegment(
            control1,
            control2,
            target,
            true));
        return new PathGeometry([figure]);
    }

    private static (Point Control1, Point Control2)
        DefaultControlPoints(Point source, Point target)
    {
        var delta = target - source;
        return (
            source + delta * 0.34,
            target - delta * 0.28);
    }

    private static Point ElementCenter(
        FrameworkElement element,
        UIElement relativeTo)
    {
        return element.TranslatePoint(
            new Point(
                element.ActualWidth / 2,
                element.ActualHeight / 2),
            relativeTo);
    }

    private static bool IsFinite(Point point)
    {
        return
            double.IsFinite(point.X) &&
            double.IsFinite(point.Y);
    }

    private IEnumerable<(
        RadialMenuSlotViewModel Slot,
        FrameworkElement Source)> SlotSources(
            RadialMenuViewModel viewModel)
    {
        yield return (viewModel.Top, TopSourceAnchor);
        yield return (viewModel.Right, RightSourceAnchor);
        yield return (viewModel.CenterRight, CenterRightSourceAnchor);
        yield return (viewModel.Bottom, BottomSourceAnchor);
        yield return (viewModel.CenterLeft, CenterLeftSourceAnchor);
        yield return (viewModel.Left, LeftSourceAnchor);
    }

    private static IEnumerable<RadialMenuSlotViewModel> Slots(
        RadialMenuViewModel viewModel)
    {
        yield return viewModel.Top;
        yield return viewModel.Right;
        yield return viewModel.CenterRight;
        yield return viewModel.Bottom;
        yield return viewModel.CenterLeft;
        yield return viewModel.Left;
    }

    private IReadOnlyDictionary<
        ControllerControlAnchor,
        FrameworkElement> TargetAnchors()
    {
        return new Dictionary<ControllerControlAnchor, FrameworkElement>
        {
            [ControllerControlAnchor.DPadUp] = DPadUpAnchor,
            [ControllerControlAnchor.DPadRight] = DPadRightAnchor,
            [ControllerControlAnchor.DPadDown] = DPadDownAnchor,
            [ControllerControlAnchor.DPadLeft] = DPadLeftAnchor,
            [ControllerControlAnchor.View] = ViewAnchor,
            [ControllerControlAnchor.Menu] = MenuAnchor,
            [ControllerControlAnchor.FaceNorth] = FaceNorthAnchor,
            [ControllerControlAnchor.FaceEast] = FaceEastAnchor,
            [ControllerControlAnchor.FaceSouth] = FaceSouthAnchor,
            [ControllerControlAnchor.FaceWest] = FaceWestAnchor,
            [ControllerControlAnchor.LeftStick] = LeftStickAnchor,
            [ControllerControlAnchor.RightStick] = RightStickAnchor,
            [ControllerControlAnchor.Guide] = GuideAnchor,
        };
    }

    private void HideAllTargetAnchors()
    {
        foreach (var anchor in TargetAnchors().Values)
        {
            anchor.Opacity = 0;
        }
    }
}
