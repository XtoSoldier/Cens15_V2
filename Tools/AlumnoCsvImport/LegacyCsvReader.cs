using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CENS15_V2.Models;
using Microsoft.VisualBasic.FileIO;

namespace AlumnoCsvImport;

public record RowError(long Row, string Field, string Code);
public record DuplicateRows(long FirstRow, long Row, bool Equivalent);
public record StudentData(string Nombres, string Apellidos, DateOnly? FechaNacimiento, Genero? Genero,
    string Domicilio, string Localidad, string Provincia, string Pais, string Telefono,
    string Email, string NombreEmergencia, string TelefonoEmergencia, bool?[] Flags);
public record ImportRow(long Row, string? Dni, string DniKey, StudentData Data);
public sealed class CsvResult
{
    public string Sha256 { get; init; } = "";
    public int TotalRead { get; set; }
    public int RepairedQuotes { get; set; }
    public List<ImportRow> Rows { get; } = [];
    public List<RowError> ValidationErrors { get; } = [];
    public List<DuplicateRows> Duplicates { get; } = [];
    public List<long> BirthDateBlankedRows { get; } = [];
    public Dictionary<string, int> FieldBlankCounts { get; } = new();
}

public static class LegacyCsvReader
{
    public const string Header = "id,nombre,apellido,dni,fecha_de_nacimiento,domicilio,loc_nac,prov_nac,pais_nac,inscripcion,tel_alumno,curso_id,email,nombre_tutor,tel_tutor,cert_estudio,pase,fot_dni,cuil,sexo,updated_at,created_at";
    public static readonly string[] IgnoredColumns = ["id", "inscripcion", "curso_id", "created_at", "updated_at"];
    public static readonly string[] FlagColumns = ["cert_estudio", "pase", "fot_dni", "cuil"];
    public static readonly string[] DocumentNames = ["Certificado de Estudio", "Pase", "Fotocopia DNI", "Cuil"];

    public static string NormalizeDni(string? value) => new((value ?? "").Trim()
        .Where(c => !char.IsWhiteSpace(c) && !char.IsPunctuation(c)).ToArray());

    public static string StudentKey(string? dni, string nombres, string apellidos, DateOnly? birth)
    {
        var document = NormalizeDni(dni);
        return document.Length > 0 && !document.All(c => c == '0') ? "dni:" + document
            : "sin-dni:" + JsonSerializer.Serialize(new[] { NormalizeWord(nombres), NormalizeWord(apellidos), birth?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "" });
    }

    public static string NormalizeWord(string value) => new(value.Trim().ToLowerInvariant()
        .Normalize(NormalizationForm.FormD).Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
        .ToArray());

    private static string Name(string value)
    {
        var culture = CultureInfo.GetCultureInfo("es-AR");
        return culture.TextInfo.ToTitleCase(value.Trim().ToLower(culture));
    }

    public static CsvResult Read(byte[] bytes, Action<long, string[]>? inspectFields = null)
    {
        var result = new CsvResult { Sha256 = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant() };
        var headers = Header.Split(',');
        foreach (var h in headers) result.FieldBlankCounts[h] = 0;
        string text;
        try
        {
            var offset = bytes.AsSpan().StartsWith(new byte[] { 0xef, 0xbb, 0xbf }) ? 3 : 0;
            text = new UTF8Encoding(false, true).GetString(bytes, offset, bytes.Length - offset);
        }
        catch (DecoderFallbackException)
        {
            result.ValidationErrors.Add(new(0, "file", "invalid_utf8"));
            return result;
        }
        text = RepairInnerQuotes(text, result);
        using var parser = new TextFieldParser(new StringReader(text))
        {
            TextFieldType = FieldType.Delimited, HasFieldsEnclosedInQuotes = true, TrimWhiteSpace = false
        };
        parser.SetDelimiters(",");
        var seen = new Dictionary<string, ImportRow>(StringComparer.Ordinal);
        try
        {
            if (!(parser.ReadFields() ?? []).SequenceEqual(headers))
            {
                result.ValidationErrors.Add(new(1, "header", "unexpected_header"));
                return result;
            }
            while (!parser.EndOfData)
            {
                var row = parser.LineNumber;
                var fields = parser.ReadFields()!;
                result.TotalRead++;
                if (fields.Length != headers.Length)
                {
                    result.ValidationErrors.Add(new(row, "record", "column_count"));
                    continue;
                }
                inspectFields?.Invoke(row, fields);
                string Get(string key)
                {
                    var value = fields[Array.IndexOf(headers, key)].Trim();
                    return value.Equals("NULL", StringComparison.OrdinalIgnoreCase) ? "" : value;
                }
                foreach (var h in headers) if (Get(h).Length == 0) result.FieldBlankCounts[h]++;
                var errorsBefore = result.ValidationErrors.Count;
                var rawDni = Get("dni");
                var normalizedDni = NormalizeDni(rawDni);
                var dni = normalizedDni.Length == 0 || normalizedDni.All(c => c == '0') ? null : rawDni;
                foreach (var h in new[] { "nombre", "apellido" })
                    if (Get(h).Length == 0) result.ValidationErrors.Add(new(row, h, "required"));
                DateOnly? birth = null;
                if (Get("fecha_de_nacimiento").Length > 0)
                {
                    if (DateOnly.TryParseExact(Get("fecha_de_nacimiento"), "yyyy-MM-dd", CultureInfo.InvariantCulture,
                        DateTimeStyles.None, out var parsed) && parsed != default && parsed <= DateOnly.FromDateTime(DateTime.UtcNow)) birth = parsed;
                    else result.ValidationErrors.Add(new(row, "fecha_de_nacimiento", "invalid_date"));
                }
                var genderValue = NormalizeWord(Get("sexo"));
                Genero? gender = genderValue switch { "varon" or "hombre" or "masculino" => Genero.Varon, "mujer" or "femenino" => Genero.Mujer, _ => null };
                if (genderValue.Length > 0 && gender == null) result.ValidationErrors.Add(new(row, "sexo", "invalid_gender"));
                var flags = new bool?[4];
                for (var i = 0; i < flags.Length; i++)
                {
                    var flag = Get(FlagColumns[i]);
                    flags[i] = flag switch { "0" => false, "1" => true, _ => null };
                    if (flag != "" && flag != "0" && flag != "1") result.ValidationErrors.Add(new(row, FlagColumns[i], "expected_0_or_1"));
                }
                if (result.ValidationErrors.Count != errorsBefore) continue;
                var data = new StudentData(Name(Get("nombre")), Name(Get("apellido")), birth, gender,
                    Get("domicilio"), Get("loc_nac"), Get("prov_nac"), Get("pais_nac"), Get("tel_alumno"),
                    Get("email"), Get("nombre_tutor"), Get("tel_tutor"), flags);
                var key = StudentKey(dni, data.Nombres, data.Apellidos, birth);
                var item = new ImportRow(row, dni, key, data);
                if (seen.TryGetValue(key, out var first))
                {
                    var equivalent = JsonSerializer.Serialize(first.Data) == JsonSerializer.Serialize(data);
                    result.Duplicates.Add(new(first.Row, row, equivalent));
                    if (!equivalent) result.ValidationErrors.Add(new(row, "dni", "contradictory_duplicate"));
                }
                else { seen.Add(key, item); result.Rows.Add(item); }
            }
        }
        catch (MalformedLineException)
        {
            result.ValidationErrors.Add(new(parser.ErrorLineNumber, "record", "malformed_csv"));
        }
        return result;
    }

    private static string RepairInnerQuotes(string text, CsvResult result)
    {
        var output = new StringBuilder(text.Length);
        var quoted = false;
        var fieldStart = true;
        var repairedField = false;
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (!quoted)
            {
                output.Append(c);
                if (c == '"' && fieldStart) { quoted = true; repairedField = false; fieldStart = false; }
                else if (c is ',' or '\r' or '\n') fieldStart = true;
                else if (!char.IsWhiteSpace(c)) fieldStart = false;
                continue;
            }
            if (c != '"') { output.Append(c); continue; }
            var pair = i + 1 < text.Length && text[i + 1] == '"';
            var next = i + (pair ? 2 : 1);
            while (next < text.Length && text[next] is ' ' or '\t') next++;
            var boundary = next == text.Length || text[next] is ',' or '\r' or '\n';
            if (pair && repairedField && boundary)
            {
                output.Append("\"\"\""); i++; quoted = false; result.RepairedQuotes++;
            }
            else if (pair) { output.Append("\"\""); i++; }
            else if (boundary) { output.Append(c); quoted = false; }
            else
            {
                // The legacy export contains literal quotes inside quoted addresses. Escape in memory only.
                output.Append("\"\""); repairedField = true; result.RepairedQuotes++;
            }
        }
        return output.ToString();
    }

    public static Alumno Map(ImportRow row, IReadOnlyDictionary<string, int> catalog)
    {
        var d = row.Data;
        var alumno = new Alumno
        {
            Nombres = d.Nombres, Apellidos = d.Apellidos, NumeroDocumento = row.Dni,
            FechaNacimiento = d.FechaNacimiento, Genero = d.Genero, Domicilio = d.Domicilio,
            DatosNacimiento = new() { Localidad = d.Localidad, Provincia = d.Provincia, Pais = d.Pais },
            Contacto = new() { TelefonoAlumno = d.Telefono, Email = d.Email,
                NombreEmergencia = d.NombreEmergencia, TelefonoEmergencia = d.TelefonoEmergencia }
        };
        for (var i = 0; i < 4; i++)
            if (d.Flags[i] is bool flag && catalog.TryGetValue(DocumentNames[i], out var id))
                alumno.Documentos.Add(new() { TipoDocumentoAlumnoId = id, Presentado = flag, ImagenUrl = null, ImagenesUrl = [] });
        return alumno;
    }
}
