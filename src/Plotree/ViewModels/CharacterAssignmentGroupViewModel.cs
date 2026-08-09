using System.Collections.ObjectModel;

namespace Plotree.ViewModels;

/// <summary>A localized group section in the character-assignment flyout.</summary>
public sealed class CharacterAssignmentGroupViewModel
{
    public required string Id { get; init; }

    public required string Name { get; init; }

    public string AutomationId => $"CharacterAssignmentGroup_{Id}";

    public ObservableCollection<CharacterOptionViewModel> Characters { get; } = [];
}
