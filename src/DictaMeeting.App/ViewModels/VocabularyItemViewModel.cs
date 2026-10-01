using System;
using System.Collections.Generic;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using DictaMeeting.Meetings.Vocabulary;

namespace DictaMeeting.App.ViewModels;

/// <summary>
/// ViewModel para representar un elemento individual del vocabulario en la interfaz.
/// </summary>
public partial class VocabularyItemViewModel : ObservableObject
{
    [ObservableProperty]
    private string _id = string.Empty;

    [ObservableProperty]
    private string _word = string.Empty;

    [ObservableProperty]
    private List<string> _aliases = new();

    public string AliasesDisplayText => Aliases != null && Aliases.Count > 0
        ? string.Join(", ", Aliases)
        : "Sin variantes fonéticas";

    public bool HasAliases => Aliases != null && Aliases.Count > 0;

    public static VocabularyItemViewModel FromModel(VocabularyTerm model)
    {
        return new VocabularyItemViewModel
        {
            Id = model.Id,
            Word = model.Word,
            Aliases = model.Aliases != null ? new List<string>(model.Aliases) : new List<string>()
        };
    }
}
