using Microsoft.EntityFrameworkCore;
using RadarChollos.Models;

namespace RadarChollos.Data;

public class AppDbContext : DbContext
{
    public DbSet<Producto> Productos => Set<Producto>();
    public DbSet<Chollo> Chollos => Set<Chollo>();

    // El constructor recibe DbContextOptions configurado desde el contenedor de dependencias (DI)
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // 1. Configuración de la entidad Chollo
        modelBuilder.Entity<Chollo>(entity =>
        {
            entity.HasKey(c => c.Id);

            // Índice único en la URL: garantiza a nivel de motor de BD que un chollo jamás se duplique
            entity.HasIndex(c => c.Enlace)
                  .IsUnique();

            // Relación 1 a N: Un Producto (regla) tiene muchos Chollos detectados.
            // Si se borra la regla, los chollos asociados ponen su ProductoId a null (no rompemos el historial)
            entity.HasOne(c => c.Producto)
                  .WithMany()
                  .HasForeignKey(c => c.ProductoId)
                  .OnDelete(DeleteBehavior.SetNull);
        });

        // 2. Configuración de la entidad Producto
        modelBuilder.Entity<Producto>(entity =>
        {
            entity.HasKey(p => p.Id);

            // Índice para agilizar búsquedas por patrón de texto
            entity.HasIndex(p => p.Patron);
        });
    }
}