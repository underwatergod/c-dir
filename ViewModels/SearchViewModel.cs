using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using CDir.Models;
using CDir.Services;

namespace CDir.ViewModels;

public sealed class SearchViewModel : INotifyPropertyChanged
{
    private readonly SearchService _service;
    private CancellationTokenSource? _cts;
    private string _query = "";
    private string _statusText = "";
    private bool _isOpen;
    private bool _isSearching;

    public SearchViewModel(SearchService service)
    {
        _service = service;
        Results.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(ResultCount));
            OnPropertyChanged(nameof(IsEmpty));
        };
    }

    public ObservableCollection<FileItem> Results { get; } = [];

    public event PropertyChangedEventHandler? PropertyChanged;

    public string Query
    {
        get => _query;
        set
        {
            if (!SetField(ref _query, value)) return;
            IsOpen = !string.IsNullOrWhiteSpace(value);
            _ = SearchNowAsync();
        }
    }

    public string StatusText
    {
        get => _statusText;
        private set => SetField(ref _statusText, value);
    }

    public bool IsOpen
    {
        get => _isOpen;
        private set => SetField(ref _isOpen, value);
    }

    public bool IsSearching
    {
        get => _isSearching;
        private set
        {
            if (!SetField(ref _isSearching, value)) return;
            OnPropertyChanged(nameof(IsEmpty));
        }
    }

    public bool IsEmpty => !IsSearching && Results.Count == 0;
    public int ResultCount => Results.Count;

    public string ProviderName => _service.ProviderName;

    public void Close()
    {
        _cts?.Cancel();
        Query = "";
        Results.Clear();
        StatusText = "";
        IsOpen = false;
    }

    public async Task SearchNowAsync()
    {
        _cts?.Cancel();
        if (string.IsNullOrWhiteSpace(_query))
        {
            Results.Clear();
            StatusText = "";
            IsOpen = false;
            return;
        }

        var cts = new CancellationTokenSource();
        _cts = cts;
        IsOpen = true;
        IsSearching = true;
        StatusText = "검색 중…";

        try
        {
            await Task.Delay(180, cts.Token);
            var outcome = await _service.SearchAsync(_query, 500, cts.Token);
            if (cts.IsCancellationRequested) return;

            Results.Clear();
            foreach (var item in outcome.Items) Results.Add(item);

            StatusText = outcome.Note is null
                ? $"{Results.Count:N0}개 결과 · {outcome.Provider}"
                : $"{Results.Count:N0}개 결과 · {outcome.Provider} · {outcome.Note}";
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (Exception ex)
        {
            StatusText = $"검색 오류: {ex.Message}";
        }
        finally
        {
            if (_cts == cts)
            {
                IsSearching = false;
                OnPropertyChanged(nameof(ProviderName));
            }
        }
    }

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(name);
        return true;
    }

    private void OnPropertyChanged(string? name) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
