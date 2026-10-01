using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.ML.Tokenizers;

namespace DictaMeeting.Transcription.Services.Punctuation;

/// <summary>
/// Tokenizer compatible con XLM-RoBERTa basado en Microsoft.ML.Tokenizers.SentencePieceTokenizer.
/// Aplica el offset de Fairseq (+1 para vocabulario regular) y mapea los tokens especiales:
/// <s>=0, <pad>=1, </s>=2, <unk>=3.
/// </summary>
public sealed class XlmRobertaTokenizer : IDisposable
{
    private readonly SentencePieceTokenizer _innerTokenizer;
    private readonly FileStream? _ownedStream;
    private bool _disposed;

    public const long BosTokenId = 0L;
    public const long PadTokenId = 1L;
    public const long EosTokenId = 2L;
    public const long UnkTokenId = 3L;

    public XlmRobertaTokenizer(string modelPath)
    {
        if (!File.Exists(modelPath))
        {
            throw new FileNotFoundException($"No se encontró el archivo SentencePiece en: {modelPath}", modelPath);
        }

        _ownedStream = File.OpenRead(modelPath);
        _innerTokenizer = SentencePieceTokenizer.Create(_ownedStream, addBeginningOfSentence: false, addEndOfSentence: false);
    }

    public XlmRobertaTokenizer(Stream modelStream)
    {
        _innerTokenizer = SentencePieceTokenizer.Create(modelStream, addBeginningOfSentence: false, addEndOfSentence: false);
    }

    /// <summary>
    /// Convierte un ID nativo de SentencePiece al ID de vocabulario de Fairseq / HuggingFace XLM-RoBERTa.
    /// </summary>
    public static long ConvertSentencePieceIdToXlmRoberta(int spId)
    {
        if (spId == 0) return UnkTokenId; // <unk>
        if (spId == 1) return BosTokenId; // <s>
        if (spId == 2) return EosTokenId; // </s>
        return spId + 1L; // Fairseq offset
    }

    /// <summary>
    /// Representa un subtokén alineado con su palabra de origen.
    /// </summary>
    public sealed class SubtokenInfo
    {
        public long TokenId { get; set; }
        public string Value { get; set; } = string.Empty;
        public int WordIndex { get; set; }
    }

    /// <summary>
    /// Tokeniza una lista de palabras y devuelve los subtokens alineados con cada palabra,
    /// junto con los arrays input_ids y attention_mask listos para ONNX Runtime (incluyendo <s> y </s>).
    /// </summary>
    public (IReadOnlyList<SubtokenInfo> Subtokens, long[] InputIds, long[] AttentionMask) EncodeWords(IReadOnlyList<string> words)
    {
        if (words == null || words.Count == 0)
        {
            return (Array.Empty<SubtokenInfo>(), new[] { BosTokenId, EosTokenId }, new[] { 1L, 1L });
        }

        var joined = string.Join(" ", words);
        var rawTokens = _innerTokenizer.EncodeToTokens(joined, out _);

        int wordIdx = -1;
        var subtokens = new List<SubtokenInfo>(rawTokens.Count);

        foreach (var rawToken in rawTokens)
        {
            // En SentencePiece, el carácter U+2581 (' ') o un espacio marca el comienzo de una nueva palabra.
            bool isNewWord = rawToken.Value.StartsWith("\u2581") || rawToken.Value.StartsWith(" ") || wordIdx == -1;

            if (isNewWord)
            {
                wordIdx++;
                if (wordIdx >= words.Count)
                {
                    wordIdx = words.Count - 1;
                }
            }

            subtokens.Add(new SubtokenInfo
            {
                TokenId = ConvertSentencePieceIdToXlmRoberta(rawToken.Id),
                Value = rawToken.Value,
                WordIndex = wordIdx
            });
        }

        // Construir input_ids con <s> al inicio y </s> al final
        var inputIds = new long[subtokens.Count + 2];
        inputIds[0] = BosTokenId;
        for (int i = 0; i < subtokens.Count; i++)
        {
            inputIds[i + 1] = subtokens[i].TokenId;
        }
        inputIds[^1] = EosTokenId;

        var attentionMask = new long[inputIds.Length];
        Array.Fill(attentionMask, 1L);

        return (subtokens, inputIds, attentionMask);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _ownedStream?.Dispose();
    }
}
