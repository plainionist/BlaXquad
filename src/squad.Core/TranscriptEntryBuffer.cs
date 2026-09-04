using System.Text;
using global::squad.Ui.Abstractions;

namespace squad.Core;

internal sealed class TranscriptEntryBuffer
{
    private const string myTruncationMarker = "[Earlier content is available in transcript history.]\n";
    private readonly StringBuilder myContent = new();
    private readonly int myMaxCharacters;
    private char[]? myTail;
    private int myTailLength;
    private int myTailStart;
    private long myTotalLength;
    private ulong myContentHash = 14695981039346656037;
    private bool myTruncated;

    internal TranscriptEntryBuffer(DateTimeOffset occurredAt, string source, int maxCharacters)
    {
        OccurredAt = occurredAt;
        Source = source;
        myMaxCharacters = maxCharacters;
    }

    private DateTimeOffset OccurredAt { get; }
    private string Source { get; }

    internal int Length => myTruncated
        ? Math.Min(myTruncationMarker.Length, myMaxCharacters) + myTailLength
        : myContent.Length;
    internal bool IsTruncated => myTruncated;
    internal long ContentStart => myTruncated ? myTotalLength - myTailLength : 0;
    internal bool Matches(string content) =>
        content.Length == myTotalLength && ComputeHash(content) == myContentHash;

    internal void Append(string content)
    {
        myTotalLength += content.Length;
        foreach (var character in content)
        {
            myContentHash ^= character;
            myContentHash *= 1099511628211;
        }
        if (myTruncated)
        {
            AppendToTail(content);
            return;
        }
        if (content.Length <= myMaxCharacters - myContent.Length)
        {
            myContent.Append(content);
            return;
        }

        var contentLimit = Math.Max(0, myMaxCharacters - myTruncationMarker.Length);
        myTail = new char[contentLimit];
        AppendToTail(myContent.ToString());
        AppendToTail(content);
        myContent.Clear();
        myTruncated = true;
    }

    internal TranscriptEntry CreateEntry()
    {
        if (!myTruncated)
            return new TranscriptEntry(OccurredAt, Source, myContent.ToString());

        var content = new StringBuilder(myMaxCharacters);
        content.Append(myTruncationMarker.AsSpan(
            0,
            Math.Min(myTruncationMarker.Length, myMaxCharacters)));
        if (myTail is not null)
        {
            var firstLength = Math.Min(myTailLength, myTail.Length - myTailStart);
            content.Append(myTail.AsSpan(myTailStart, firstLength));
            content.Append(myTail.AsSpan(0, myTailLength - firstLength));
        }
        return new TranscriptEntry(OccurredAt, Source, content.ToString());
    }

    private void AppendToTail(string content)
    {
        if (myTail is null || myTail.Length == 0)
            return;
        foreach (var character in content)
        {
            if (myTailLength < myTail.Length)
            {
                myTail[(myTailStart + myTailLength) % myTail.Length] = character;
                myTailLength++;
                continue;
            }
            myTail[myTailStart] = character;
            myTailStart = (myTailStart + 1) % myTail.Length;
        }
    }

    private static ulong ComputeHash(string content)
    {
        var hash = 14695981039346656037UL;
        foreach (var character in content)
        {
            hash ^= character;
            hash *= 1099511628211;
        }
        return hash;
    }
}



