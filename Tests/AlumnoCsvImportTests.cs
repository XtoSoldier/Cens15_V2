using System.Text;
using AlumnoCsvImport;
using CENS15_V2.Models;
using Xunit;

namespace CENS15_V2.Tests;

public class AlumnoCsvImportTests
{
    private static string Row(params (string Key, string Value)[] changes)
    {
        var values = LegacyCsvReader.Header.Split(',').ToDictionary(h => h, _ => "");
        values["nombre"] = "MARÍA";
        values["apellido"] = "PÉREZ";
        values["dni"] = " 00.123.456 ";
        values["fecha_de_nacimiento"] = "2001-02-03";
        values["sexo"] = "VaRÓN";
        values["tel_alumno"] = "001234";
        foreach (var (key, value) in changes) values[key] = value;
        return string.Join(",", LegacyCsvReader.Header.Split(',').Select(h => "\"" + values[h].Replace("\"", "\"\"") + "\""));
    }

    private static CsvResult Read(params string[] rows) => LegacyCsvReader.Read(Encoding.UTF8.GetBytes(
        LegacyCsvReader.Header + "\r\n" + string.Join("\r\n", rows) + "\r\n"));

    [Fact]
    public void MapsQuotedUtf8StringsAndNeverEnrolls()
    {
        var result = Read(Row(("domicilio", "Calle, \"Sur\"\nDepto 1"), ("inscripcion", "1"), ("curso_id", "999"), ("id", "999")));
        Assert.Empty(result.ValidationErrors);
        var row = Assert.Single(result.Rows);
        var alumno = LegacyCsvReader.Map(row, new Dictionary<string, int>());
        Assert.Equal("María", alumno.Nombres);
        Assert.Equal("Pérez", alumno.Apellidos);
        Assert.Equal("00.123.456", alumno.NumeroDocumento);
        Assert.Equal("dni:00123456", row.DniKey);
        Assert.Equal("001234", alumno.Contacto.TelefonoAlumno);
        Assert.Equal("Calle, \"Sur\"\nDepto 1", alumno.Domicilio);
        Assert.Equal(Genero.Varon, alumno.Genero);
        Assert.Equal(new DateOnly(2001, 2, 3), alumno.FechaNacimiento);
        Assert.Equal("", alumno.DatosNacimiento.Pais);
        Assert.Empty(alumno.Inscripciones);
        Assert.Equal(0, alumno.Id);
    }

    [Theory]
    [InlineData("fecha_de_nacimiento", "2001-02-30")]
    [InlineData("fecha_de_nacimiento", "03/02/2001")]
    [InlineData("sexo", "otro")]
    [InlineData("nombre", " ")]
    [InlineData("cert_estudio", "true")]
    public void InvalidRequiredFieldsAndFlagsBlockRows(string field, string value)
    {
        var result = Read(Row((field, value)));
        Assert.Empty(result.Rows);
        Assert.Contains(result.ValidationErrors, e => e.Row == 2 && e.Field == field);
    }

    [Fact]
    public void MissingValuesAndLegacyNullMarkersRemainBlankWithoutInventingData()
    {
        var result = Read(Row(("dni", "0"), ("fecha_de_nacimiento", "NULL"), ("sexo", "NULL"),
            ("domicilio", "NULL"), ("email", "NULL"), ("cert_estudio", "NULL")));
        Assert.Empty(result.ValidationErrors);
        var alumno = LegacyCsvReader.Map(Assert.Single(result.Rows), new Dictionary<string, int>());
        Assert.Null(alumno.NumeroDocumento); Assert.Null(alumno.FechaNacimiento); Assert.Null(alumno.Genero);
        Assert.Equal("", alumno.Domicilio); Assert.Equal("", alumno.Contacto.Email); Assert.Empty(alumno.Documentos);
        Assert.Empty(alumno.Inscripciones);
    }

    [Fact]
    public void HombreIsADeclaredMaleValueAndMissingDocumentsDoNotCollapseDifferentStudents()
    {
        var result = Read(Row(("sexo", "hombre"), ("dni", "0")), Row(("nombre", "Ana"), ("dni", "0")));
        Assert.Empty(result.ValidationErrors);
        Assert.Equal(2, result.Rows.Count);
        Assert.Equal(Genero.Varon, result.Rows[0].Data.Genero);
    }

    [Fact]
    public void LegacyUnescapedAddressQuotesAreRepairedWithoutChangingTheAddress()
    {
        var row = Row(("domicilio", "Direccion P.B. \"B\""));
        row = row.Replace("\"Direccion P.B. \"\"B\"\"\"", "\"Direccion P.B. \"B\"\"");
        var result = Read(row);
        Assert.Empty(result.ValidationErrors);
        Assert.Equal("Direccion P.B. \"B\"", Assert.Single(result.Rows).Data.Domicilio);
        Assert.Equal(2, result.RepairedQuotes);
    }

    [Fact]
    public void EquivalentDuplicatesIgnoreLegacyColumnsAndDniPunctuation()
    {
        var result = Read(Row(), Row(("dni", "00123456"), ("curso_id", "different"), ("created_at", "invalid ignored")));
        Assert.Empty(result.ValidationErrors);
        Assert.Single(result.Rows);
        Assert.Equal(new DuplicateRows(2, 3, true), Assert.Single(result.Duplicates));
    }

    [Theory]
    [InlineData("email", "different@example.invalid")]
    [InlineData("cert_estudio", "1")]
    [InlineData("sexo", "mujer")]
    public void ContradictoryDuplicatesBlockEvenWhenFlagsCannotMap(string field, string value)
    {
        var result = Read(Row(), Row((field, value)));
        Assert.False(Assert.Single(result.Duplicates).Equivalent);
        Assert.Contains(result.ValidationErrors, e => e.Code == "contradictory_duplicate" && e.Row == 3);
    }

    [Fact]
    public void FlagsUseOnlyCurrentCatalogAndPreserveFalse()
    {
        var row = Assert.Single(Read(Row(("cert_estudio", "1"), ("pase", "0"), ("fot_dni", "1"))).Rows);
        var alumno = LegacyCsvReader.Map(row, new Dictionary<string, int> { ["Certificado de Estudio"] = 10, ["Pase"] = 20, ["Cuil"] = 40 });
        Assert.Equal(2, alumno.Documentos.Count);
        Assert.Contains(alumno.Documentos, d => d.TipoDocumentoAlumnoId == 10 && d.Presentado);
        Assert.Contains(alumno.Documentos, d => d.TipoDocumentoAlumnoId == 20 && !d.Presentado);
        Assert.All(alumno.Documentos, d => { Assert.Null(d.ImagenUrl); Assert.Empty(d.ImagenesUrl); });
    }

    [Fact]
    public void Utf8BomAcceptedButInvalidUtf8Rejected()
    {
        var bytes = Encoding.UTF8.GetBytes(LegacyCsvReader.Header + "\n" + Row());
        Assert.Empty(LegacyCsvReader.Read(new byte[] { 0xef, 0xbb, 0xbf }.Concat(bytes).ToArray()).ValidationErrors);
        Assert.Contains(LegacyCsvReader.Read(bytes.Concat(new byte[] { 0xff }).ToArray()).ValidationErrors, e => e.Code == "invalid_utf8");
    }

    [Fact]
    public void ExactHeaderAndColumnCountRequired()
    {
        Assert.Contains(LegacyCsvReader.Read(Encoding.UTF8.GetBytes("wrong\n")).ValidationErrors, e => e.Field == "header");
        Assert.Contains(Read("a,b").ValidationErrors, e => e.Code == "column_count");
        Assert.Contains(Read("\"unterminated").ValidationErrors, e => e.Code == "malformed_csv");
    }

    [Theory]
    [InlineData("remote.example")]
    [InlineData("localhost,remote.example")]
    [InlineData("127.0.0.2")]
    [InlineData("/tmp")]
    public void RemoteTargetsBlockedBeforeConnection(string host) =>
        Assert.Throws<InvalidOperationException>(() => ImportCli.LocalTarget($"Host={host};Database=test"));

    [Theory]
    [InlineData("localhost")]
    [InlineData("127.0.0.1")]
    [InlineData("::1")]
    public void ExplicitLoopbackTargetsAccepted(string host) =>
        Assert.Equal(host, ImportCli.LocalTarget($"Host={host};Database=test").Host);
}
