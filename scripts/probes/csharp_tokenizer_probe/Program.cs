// L4 Tokenizer Divergence Probe -- C# side (Coda, 2026-05-06).
//
// Goal: tokenize each prose field of every record in
// parity/python_reference_v0.1.json with the EXACT same
// Microsoft.ML.Tokenizers.BertTokenizer + BertOptions config Rune uses in
// MiniLmEncoder, and dump the resulting input_ids to a JSON file. The Python
// probe then compares against HuggingFace BertTokenizerFast (the path Tess
// uses to generate the fixture).
//
// Mirror of MiniLmEncoder.cs, lines 107-115:
//     LowerCaseBeforeTokenization = true,
//     ApplyBasicTokenization      = true,
//     RemoveNonSpacingMarks       = true,
//     SplitOnSpecialTokens        = true,
//     IndividuallyTokenizeCjk     = false,
// And EncodeToIds(text,
//     addSpecialTokens: true,
//     considerPreTokenization: true,
//     considerNormalization: true).
//
// Also truncated to MaxSeqLen = 256 like MiniLmEncoder does.

using System.Text.Json;
using Microsoft.ML.Tokenizers;

const int MaxSeqLen = 256;

if (args.Length < 3)
{
    Console.Error.WriteLine("Usage: CsharpTokenizerProbe <vocab_path> <fixture_path> <output_json>");
    return 2;
}

string vocabPath   = args[0];
string fixturePath = args[1];
string outputPath  = args[2];

if (!File.Exists(vocabPath))   { Console.Error.WriteLine($"vocab not found: {vocabPath}");   return 2; }
if (!File.Exists(fixturePath)) { Console.Error.WriteLine($"fixture not found: {fixturePath}"); return 2; }

var bertOptions = new BertOptions
{
    LowerCaseBeforeTokenization = true,
    ApplyBasicTokenization      = true,
    RemoveNonSpacingMarks       = true,
    SplitOnSpecialTokens        = true,
    IndividuallyTokenizeCjk     = false,
};
var tokenizer = BertTokenizer.Create(vocabPath, bertOptions);
Console.WriteLine($"BertTokenizer loaded from {vocabPath}");

string fixtureJson = File.ReadAllText(fixturePath);
using var fixtureDoc = JsonDocument.Parse(fixtureJson);
var records = fixtureDoc.RootElement.GetProperty("records");
Console.WriteLine($"Loaded {records.GetArrayLength()} records.");

string[] proseFields = ["prose_character", "prose_action", "prose_context"];

var output = new Dictionary<string, Dictionary<string, int[]>>();

foreach (var rec in records.EnumerateArray())
{
    string id = rec.GetProperty("id").GetString()!;
    var fields = new Dictionary<string, int[]>();
    foreach (var field in proseFields)
    {
        string text = rec.GetProperty(field).GetString()!;
        IReadOnlyList<int> ids = tokenizer.EncodeToIds(
            text,
            addSpecialTokens:        true,
            considerPreTokenization: true,
            considerNormalization:   true);
        if (ids.Count > MaxSeqLen)
            ids = ids.Take(MaxSeqLen).ToList();
        fields[field] = ids.ToArray();
    }
    output[id] = fields;
}

string outJson = JsonSerializer.Serialize(output, new JsonSerializerOptions { WriteIndented = true });
File.WriteAllText(outputPath, outJson);
Console.WriteLine($"Wrote {output.Count} records to {outputPath}");
return 0;
