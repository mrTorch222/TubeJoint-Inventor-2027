using System.Runtime.CompilerServices;
using Inventor;

namespace TubeJoint.AddIn.TubeAnalysis;

/// <summary>
/// Per-document cache. Inventor changes ModelGeometryVersion whenever the B-Rep
/// changes, so property/UI refreshes can reuse analysis without stale geometry.
/// ConditionalWeakTable avoids keeping closed COM documents alive.
/// </summary>
internal sealed class CachedTubeAnalyzer : ITubeAnalyzer
{
    private readonly ITubeAnalyzer _inner;
    private readonly ConditionalWeakTable<PartDocument, CacheEntry> _entries = new();
    private readonly object _sync = new();

    public CachedTubeAnalyzer(ITubeAnalyzer inner) => _inner = inner;

    public TubeAnalysisResult Analyze(PartDocument document)
    {
        var geometryVersion = document.ComponentDefinition.ModelGeometryVersion;
        lock (_sync)
        {
            if (_entries.TryGetValue(document, out var entry) &&
                string.Equals(entry.GeometryVersion, geometryVersion, StringComparison.Ordinal))
                return entry.GetResult();
        }

        try
        {
            var result = _inner.Analyze(document);
            Store(document, new CacheEntry(result.Diagnostics.GeometryVersion, result, null));
            return result;
        }
        catch (TubeAnalysisException exception)
        {
            Store(document, new CacheEntry(
                geometryVersion, null, new CachedFailure(exception.Failure, exception.Message)));
            throw;
        }
    }

    private void Store(PartDocument document, CacheEntry entry)
    {
        lock (_sync)
        {
            _entries.Remove(document);
            _entries.Add(document, entry);
        }
    }

    private sealed record CacheEntry(
        string GeometryVersion,
        TubeAnalysisResult? Result,
        CachedFailure? Failure)
    {
        public TubeAnalysisResult GetResult()
        {
            if (Result is not null) return Result;
            throw new TubeAnalysisException(Failure!.Failure, Failure.Message);
        }
    }

    private sealed record CachedFailure(TubeAnalysisFailure Failure, string Message);
}
