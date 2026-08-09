using CommunityToolkit.Mvvm.ComponentModel;
using Plotree.Models;

namespace Plotree.ViewModels;

/// <summary>Observable wrapper around a <see cref="PlotEdge"/> with resolved endpoints.</summary>
public partial class EdgeViewModel : ObservableObject
{
    private readonly MainPageViewModel _owner;

    public EdgeViewModel(PlotEdge model, NodeViewModel from, NodeViewModel to, MainPageViewModel owner)
    {
        Model = model;
        From = from;
        To = to;
        _owner = owner;
    }

    public PlotEdge Model { get; }
    public NodeViewModel From { get; }
    public NodeViewModel To { get; }

    public string LabelText
    {
        get => Model.Label ?? string.Empty;
        set
        {
            var normalized = string.IsNullOrWhiteSpace(value) ? null : value;
            if (Model.Label == normalized)
            {
                return;
            }

            var key = _owner.GetEdgeTextEditKey(this);
            var before = _owner.CaptureProjectBeforeChange(key);
            Model.Label = normalized;
            OnPropertyChanged();
            _owner.CompleteProjectMutation(before, key);
        }
    }

    [ObservableProperty]
    public partial bool IsSelected { get; set; }
}
