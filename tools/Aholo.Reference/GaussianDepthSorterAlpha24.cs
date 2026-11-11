// Frozen verbatim from source commit 9f7fb3b; only class visibility/name differ.
using Aholo.Core;

internal static class GaussianDepthSorterAlpha24
{
    /// <summary>
    /// Back-to-front depth, then reverse source insertion order to match the CPU oracle's stable front-to-back compositor.
    /// Cancellation may partially reorder indices; discard the interrupted generation rather than publishing it.
    /// </summary>
    public static void SortBackToFront(Span<int> indices, ReadOnlySpan<GaussianProjectedValue> values, Span<int> scratch, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        if (scratch.Length < indices.Length) throw new ArgumentException("Sort scratch must be at least as long as the index range.", nameof(scratch));
        if (indices.Overlaps(scratch)) throw new ArgumentException("Sort scratch must not overlap the indices.", nameof(scratch));
        for (int i = 0; i < indices.Length; ++i)
        {
            if ((i & 1023) == 0) ct.ThrowIfCancellationRequested();
            int index = indices[i];
            if ((uint)index >= (uint)values.Length || !double.IsFinite(values[index].Depth) || values[index].Depth <= 0)
                throw new ArgumentException("Every sort index must reference a finite positive projected depth.", nameof(indices));
        }
        if (indices.Length < 2) return;
        Span<int> source = indices, destination = scratch[..indices.Length];
        bool sourceIsIndices = true;
        for (int width = 1; width < indices.Length; width = width > indices.Length / 2 ? indices.Length : width * 2)
        {
            ct.ThrowIfCancellationRequested();
            int start = 0;
            while (start < indices.Length)
            {
                ct.ThrowIfCancellationRequested();
                int middle = start + Math.Min(width, indices.Length - start), end = middle + Math.Min(width, indices.Length - middle);
                int left = start, right = middle;
                for (int output = start; output < end; ++output)
                {
                    if ((output & 1023) == 0) ct.ThrowIfCancellationRequested();
                    bool takeLeft = right >= end || (left < middle && BeforeOrEqual(in values[source[left]], in values[source[right]]));
                    destination[output] = takeLeft ? source[left++] : source[right++];
                }
                start = end;
            }
            Span<int> temp = source; source = destination; destination = temp; sourceIsIndices = !sourceIsIndices;
        }
        if (!sourceIsIndices)
            for (int i = 0; i < indices.Length; i += Math.Min(1024, indices.Length - i))
            {
                ct.ThrowIfCancellationRequested(); int length = Math.Min(1024, indices.Length - i); source.Slice(i, length).CopyTo(indices.Slice(i, length));
            }
        ct.ThrowIfCancellationRequested();
    }
    private static bool BeforeOrEqual(in GaussianProjectedValue left, in GaussianProjectedValue right) =>
        left.Depth > right.Depth || (left.Depth == right.Depth && left.GlobalOrdinal >= right.GlobalOrdinal);
}
