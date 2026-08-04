namespace Vecxy.UI;

public interface IUiDiagnostics
{
    UiPerformanceStatistics Statistics { get; }
}

public sealed class UiTimingStatistics
{
    private long _samples;

    public double CurrentMilliseconds { get; private set; }
    public double AverageMilliseconds { get; private set; }
    public double PeakMilliseconds { get; private set; }

    internal void Record(double milliseconds)
    {
        CurrentMilliseconds = milliseconds;
        AverageMilliseconds = _samples++ == 0
            ? milliseconds
            : AverageMilliseconds * 0.95 + milliseconds * 0.05;
        PeakMilliseconds = Math.Max(PeakMilliseconds, milliseconds);
    }

    internal void ResetPeak() => PeakMilliseconds = CurrentMilliseconds;
}

public sealed class UiDocumentStatistics
{
    private bool _hasVersions;
    private int _previousStyleVersion;
    private int _previousLayoutVersion;
    private int _previousVisualVersion;

    public required string Path { get; init; }
    public bool Visible { get; internal set; }
    public bool RebuiltThisFrame { get; internal set; }
    public long Rebuilds { get; internal set; }
    public int CacheHits { get; internal set; }
    public long LastRebuildFrame { get; internal set; }
    public int Elements { get; internal set; }
    public int VisibleElements { get; internal set; }
    public int InteractiveElements { get; internal set; }
    public int TextElements { get; internal set; }
    public int ImageElements { get; internal set; }
    public int ShadowDefinitions { get; internal set; }
    public int ShadowLayers { get; internal set; }
    public int ActiveAnimations { get; internal set; }
    public int Vertices { get; internal set; }
    public int Indices { get; internal set; }
    public int Batches { get; internal set; }
    public int TextureSwitches { get; internal set; }
    public int RoundedClipBatches { get; internal set; }
    public int StyleVersion { get; internal set; }
    public int LayoutVersion { get; internal set; }
    public int VisualVersion { get; internal set; }
    public int StyleChangesThisFrame { get; private set; }
    public int LayoutChangesThisFrame { get; private set; }
    public int VisualChangesThisFrame { get; private set; }
    public long StylePasses { get; internal set; }
    public long LayoutPasses { get; internal set; }
    public long AnimationTreeScans { get; internal set; }
    public int LayerWidth { get; internal set; }
    public int LayerHeight { get; internal set; }
    public long LayerBytes { get; internal set; }
    public long ContentPixels { get; internal set; }
    public long UploadBytes { get; internal set; }

    internal void UpdateVersions(int style, int layout, int visual)
    {
        StyleChangesThisFrame = _hasVersions ? unchecked(style - _previousStyleVersion) : 0;
        LayoutChangesThisFrame = _hasVersions ? unchecked(layout - _previousLayoutVersion) : 0;
        VisualChangesThisFrame = _hasVersions ? unchecked(visual - _previousVisualVersion) : 0;
        StyleVersion = _previousStyleVersion = style;
        LayoutVersion = _previousLayoutVersion = layout;
        VisualVersion = _previousVisualVersion = visual;
        _hasVersions = true;
    }
}

public sealed class UiPerformanceStatistics
{
    private readonly List<UiDocumentStatistics> _documents = [];
    private readonly Dictionary<UiDocument, UiDocumentStatistics> _documentsByInstance =
        new(ReferenceEqualityComparer.Instance);

    public long Frame { get; private set; }
    public float FrameDeltaMilliseconds { get; private set; }
    public IReadOnlyList<UiDocumentStatistics> Documents => _documents;
    public UiTimingStatistics UpdateCpu { get; } = new();
    public UiTimingStatistics LayoutCpu { get; } = new();
    public UiTimingStatistics AnimationCpu { get; } = new();
    public UiTimingStatistics HitTestCpu { get; } = new();
    public UiTimingStatistics InputCpu { get; } = new();
    public UiTimingStatistics RenderCpu { get; } = new();
    public UiTimingStatistics TessellationCpu { get; } = new();
    public UiTimingStatistics UploadCpu { get; } = new();
    public UiTimingStatistics LayerDrawCpu { get; } = new();
    public UiTimingStatistics CompositeCpu { get; } = new();
    public int ActiveDocuments { get; private set; }
    public int Elements { get; private set; }
    public int VisibleElements { get; private set; }
    public int InteractiveElements { get; private set; }
    public int ActiveAnimations { get; internal set; }
    public int Vertices { get; private set; }
    public int Indices { get; private set; }
    public int Batches { get; private set; }
    public int TextureSwitches { get; private set; }
    public int ShadowDefinitions { get; private set; }
    public int ShadowLayers { get; private set; }
    public int LayerRebuilds { get; private set; }
    public int LayerCacheHits { get; private set; }
    public int CompositeDrawCalls { get; private set; }
    public long LayerMemoryBytes { get; private set; }
    public long ContentPixels { get; private set; }
    public long UploadBytes { get; private set; }
    public long UpdateAllocatedBytes { get; private set; }
    public long RenderAllocatedBytes { get; private set; }
    public double AverageUpdateAllocatedBytes { get; private set; }
    public double AverageRenderAllocatedBytes { get; private set; }
    public long PeakUpdateAllocatedBytes { get; private set; }
    public long PeakRenderAllocatedBytes { get; private set; }
    public long TotalLayerRebuilds { get; private set; }
    public long TotalLayerCacheHits { get; private set; }

    internal void BeginFrame(float deltaTime)
    {
        Frame++;
        FrameDeltaMilliseconds = deltaTime * 1000.0f;
        ActiveDocuments = 0;
        Elements = 0;
        VisibleElements = 0;
        InteractiveElements = 0;
        ActiveAnimations = 0;
        Vertices = 0;
        Indices = 0;
        Batches = 0;
        TextureSwitches = 0;
        ShadowDefinitions = 0;
        ShadowLayers = 0;
        LayerRebuilds = 0;
        LayerCacheHits = 0;
        CompositeDrawCalls = 0;
        LayerMemoryBytes = 0;
        ContentPixels = 0;
        UploadBytes = 0;
        UpdateAllocatedBytes = 0;
        RenderAllocatedBytes = 0;
        foreach (var document in _documents)
            document.RebuiltThisFrame = false;
    }

    internal void RecordUpdate(
        double totalMilliseconds,
        double layoutMilliseconds,
        double animationMilliseconds,
        double hitTestMilliseconds,
        double inputMilliseconds,
        long allocatedBytes)
    {
        UpdateCpu.Record(totalMilliseconds);
        LayoutCpu.Record(layoutMilliseconds);
        AnimationCpu.Record(animationMilliseconds);
        HitTestCpu.Record(hitTestMilliseconds);
        InputCpu.Record(inputMilliseconds);
        UpdateAllocatedBytes = allocatedBytes;
        AverageUpdateAllocatedBytes = Frame == 1
            ? allocatedBytes
            : AverageUpdateAllocatedBytes * 0.95 + allocatedBytes * 0.05;
        PeakUpdateAllocatedBytes = Math.Max(PeakUpdateAllocatedBytes, allocatedBytes);
    }

    internal UiDocumentStatistics GetDocument(UiDocument document)
    {
        if (_documentsByInstance.TryGetValue(document, out var statistics))
            return statistics;
        statistics = new UiDocumentStatistics { Path = document.Path };
        _documentsByInstance.Add(document, statistics);
        _documents.Add(statistics);
        return statistics;
    }

    internal void CompleteRender(
        IReadOnlyList<UiDocument> liveDocuments,
        double renderMilliseconds,
        double tessellationMilliseconds,
        double uploadMilliseconds,
        double layerDrawMilliseconds,
        double compositeMilliseconds,
        long allocatedBytes)
    {
        for (var index = _documents.Count - 1; index >= 0; index--)
        {
            var entry = _documents[index];
            UiDocument? key = null;
            foreach (var pair in _documentsByInstance)
            {
                if (ReferenceEquals(pair.Value, entry))
                {
                    key = pair.Key;
                    break;
                }
            }
            if (key is not null && liveDocuments.Contains(key))
                continue;
            _documents.RemoveAt(index);
            if (key is not null)
                _documentsByInstance.Remove(key);
        }

        RenderCpu.Record(renderMilliseconds);
        TessellationCpu.Record(tessellationMilliseconds);
        UploadCpu.Record(uploadMilliseconds);
        LayerDrawCpu.Record(layerDrawMilliseconds);
        CompositeCpu.Record(compositeMilliseconds);
        RenderAllocatedBytes = allocatedBytes;
        AverageRenderAllocatedBytes = Frame == 1
            ? allocatedBytes
            : AverageRenderAllocatedBytes * 0.95 + allocatedBytes * 0.05;
        PeakRenderAllocatedBytes = Math.Max(PeakRenderAllocatedBytes, allocatedBytes);
    }

    internal void Accumulate(UiDocumentStatistics document)
    {
        if (!document.Visible)
            return;
        ActiveDocuments++;
        Elements += document.Elements;
        VisibleElements += document.VisibleElements;
        InteractiveElements += document.InteractiveElements;
        ActiveAnimations += document.ActiveAnimations;
        Vertices += document.Vertices;
        Indices += document.Indices;
        Batches += document.Batches;
        TextureSwitches += document.TextureSwitches;
        ShadowDefinitions += document.ShadowDefinitions;
        ShadowLayers += document.ShadowLayers;
        LayerMemoryBytes += document.LayerBytes;
        ContentPixels += document.ContentPixels;
        UploadBytes += document.UploadBytes;
        if (document.RebuiltThisFrame)
        {
            LayerRebuilds++;
            TotalLayerRebuilds++;
        }
        else
        {
            LayerCacheHits++;
            TotalLayerCacheHits++;
        }
        if (document.ContentPixels > 0)
            CompositeDrawCalls++;
    }

    public void ResetPeaks()
    {
        UpdateCpu.ResetPeak();
        LayoutCpu.ResetPeak();
        AnimationCpu.ResetPeak();
        HitTestCpu.ResetPeak();
        InputCpu.ResetPeak();
        RenderCpu.ResetPeak();
        TessellationCpu.ResetPeak();
        UploadCpu.ResetPeak();
        LayerDrawCpu.ResetPeak();
        CompositeCpu.ResetPeak();
        PeakUpdateAllocatedBytes = UpdateAllocatedBytes;
        PeakRenderAllocatedBytes = RenderAllocatedBytes;
    }
}
