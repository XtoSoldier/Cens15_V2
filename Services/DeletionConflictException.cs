namespace CENS15_V2.Services;

public sealed class DeletionConflictException : InvalidOperationException
{
    public DeletionConflictException() : base("No se puede eliminar: existen dependencias o un conflicto concurrente. Retire los hijos manualmente desde la entidad mas hija y vuelva a consultar.") { }
}
