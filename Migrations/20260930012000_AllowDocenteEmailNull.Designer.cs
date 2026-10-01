using CENS15_V2.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CENS15_V2.Migrations
{
    [DbContext(typeof(AppDbContext))]
    [Migration("20260930012000_AllowDocenteEmailNull")]
    public partial class AllowDocenteEmailNull
    {
    }
}
