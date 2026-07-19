using CodexController.Models;
using CodexController.Controllers;

namespace CodexController.ViewModels;

public sealed class RadialMenuSlotViewModel : ObservableObject
{
    private string _id = string.Empty;
    private bool _isPresent;
    private string _inputGlyph = string.Empty;
    private string _title = string.Empty;
    private string _subtitle = string.Empty;
    private bool _isActionEnabled;
    private bool _isHighlighted;
    private double _confirmationProgress;
    private LogicalInput? _logicalInput;
    private ThreadStatus _status = ThreadStatus.Unassigned;

    public RadialMenuSlotViewModel(
        RadialMenuSlotPosition position)
    {
        Position = position;
    }

    public RadialMenuSlotPosition Position { get; }

    public string Id
    {
        get => _id;
        private set => SetProperty(ref _id, value);
    }

    public bool IsPresent
    {
        get => _isPresent;
        private set => SetProperty(ref _isPresent, value);
    }

    public string InputGlyph
    {
        get => _inputGlyph;
        private set => SetProperty(ref _inputGlyph, value);
    }

    public string Title
    {
        get => _title;
        private set
        {
            if (SetProperty(ref _title, value))
            {
                OnPropertyChanged(nameof(AccessibleName));
            }
        }
    }

    public string Subtitle
    {
        get => _subtitle;
        private set
        {
            if (SetProperty(ref _subtitle, value))
            {
                OnPropertyChanged(nameof(HasSubtitle));
                OnPropertyChanged(nameof(AccessibleName));
            }
        }
    }

    public bool HasSubtitle => !string.IsNullOrWhiteSpace(Subtitle);

    public bool IsActionEnabled
    {
        get => _isActionEnabled;
        private set
        {
            if (SetProperty(ref _isActionEnabled, value))
            {
                OnPropertyChanged(nameof(AccessibleName));
            }
        }
    }

    public bool IsHighlighted
    {
        get => _isHighlighted;
        private set => SetProperty(ref _isHighlighted, value);
    }

    public LogicalInput? LogicalInput
    {
        get => _logicalInput;
        private set
        {
            SetProperty(ref _logicalInput, value);
        }
    }

    public ThreadStatus Status
    {
        get => _status;
        private set => SetProperty(ref _status, value);
    }

    public double ConfirmationProgress
    {
        get => _confirmationProgress;
        private set
        {
            if (SetProperty(ref _confirmationProgress, value))
            {
                OnPropertyChanged(nameof(HasConfirmationProgress));
            }
        }
    }

    public bool HasConfirmationProgress => ConfirmationProgress > 0;

    public string AccessibleName
    {
        get
        {
            var description = HasSubtitle
                ? $"{Title}, {Subtitle}"
                : Title;
            var keyedDescription = string.IsNullOrWhiteSpace(InputGlyph)
                ? description
                : $"{InputGlyph}, {description}";
            return IsActionEnabled
                ? keyedDescription
                : $"{keyedDescription}, unavailable";
        }
    }

    internal void Update(RadialMenuItemState? item)
    {
        if (item is null)
        {
            Clear();
            return;
        }

        IsPresent = true;
        Id = item.Id;
        InputGlyph = item.InputGlyph;
        Title = item.Title;
        Subtitle = item.Subtitle;
        IsActionEnabled = item.IsEnabled;
        IsHighlighted = item.IsHighlighted;
        LogicalInput = item.LogicalInput;
        Status = item.Status;
        ConfirmationProgress = item.ConfirmationProgress;
    }

    private void Clear()
    {
        IsPresent = false;
        Id = string.Empty;
        InputGlyph = string.Empty;
        Title = string.Empty;
        Subtitle = string.Empty;
        IsActionEnabled = false;
        IsHighlighted = false;
        LogicalInput = null;
        Status = ThreadStatus.Unassigned;
        ConfirmationProgress = 0;
    }

    internal void SetHighlighted(bool isHighlighted)
    {
        IsHighlighted = isHighlighted;
    }
}
