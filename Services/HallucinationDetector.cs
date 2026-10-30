using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace LockStepAI.Services
{
    public sealed record HallucinationDetection(int TruncateAt, string Reason);

    public sealed class HallucinationDetector
    {
        private static readonly Regex WordPattern = new(@"\b[\p{L}\p{N}_'-]+\b", RegexOptions.Compiled);
        private static readonly Regex RepeatedCharacterPattern = new(@"(.)\1{15,}", RegexOptions.Compiled);
        private int _lastCheckedLength;

        public HallucinationDetection? Check(string currentText)
        {
            if (currentText.Length - _lastCheckedLength < 20) return null;
            _lastCheckedLength = currentText.Length;

            var repeatedCharacters = RepeatedCharacterPattern.Match(currentText);
            if (repeatedCharacters.Success)
                return new HallucinationDetection(repeatedCharacters.Index, "A character repeated excessively");

            Match[] matches = WordPattern.Matches(currentText).Cast<Match>().ToArray();
            if (matches.Length < 8) return null;

            string[] words = matches.Select(match => match.Value.ToLowerInvariant()).ToArray();
            int consecutiveCount = 1;
            for (int index = words.Length - 1; index > 0 && words[index] == words[index - 1]; index--)
                consecutiveCount++;

            if (consecutiveCount >= 8)
                return new HallucinationDetection(matches[^consecutiveCount].Index, "The same word repeated consecutively");

            int maxPhraseLength = Math.Min(24, words.Length / 3);
            for (int phraseLength = 3; phraseLength <= maxPhraseLength; phraseLength++)
            {
                int start = words.Length - phraseLength * 3;
                bool repeated = true;
                for (int offset = 0; offset < phraseLength && repeated; offset++)
                {
                    string word = words[start + offset];
                    repeated = word == words[start + phraseLength + offset]
                        && word == words[start + phraseLength * 2 + offset];
                }

                if (repeated)
                    return new HallucinationDetection(matches[start].Index, "A phrase repeated three times");
            }

            const int entropyWindowSize = 24;
            if (words.Length >= entropyWindowSize)
            {
                int start = words.Length - entropyWindowSize;
                var counts = new Dictionary<string, int>(StringComparer.Ordinal);
                for (int index = start; index < words.Length; index++)
                    counts[words[index]] = counts.GetValueOrDefault(words[index]) + 1;

                double entropy = counts.Values.Sum(count =>
                {
                    double probability = (double)count / entropyWindowSize;
                    return -probability * Math.Log2(probability);
                });

                if (counts.Count <= 5 && entropy <= 1.15)
                    return new HallucinationDetection(matches[start].Index, "Recent word entropy was unusually low");
            }

            return null;
        }
    }
}