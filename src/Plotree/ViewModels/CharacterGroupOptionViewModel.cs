using CommunityToolkit.Mvvm.ComponentModel;
using Plotree.Models;

namespace Plotree.ViewModels;

/// <summary>Two-state group membership row for the character selected in the people manager.</summary>
public partial class CharacterGroupOptionViewModel : ObservableObject
{
    private readonly MainPageViewModel _owner;

    public CharacterGroupOptionViewModel(CharacterGroup model, MainPageViewModel owner)
    {
        Model = model;
        _owner = owner;
    }

    public CharacterGroup Model { get; }
    public string Id => Model.Id;
    public string Name => Model.Name;
    public string AutomationId => $"CharacterGroupOption_{Id}";

    [ObservableProperty]
    public partial bool IsChecked { get; set; }

    partial void OnIsCheckedChanged(bool value) => _owner.OnCharacterGroupToggled(this, value);
}
