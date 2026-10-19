using Microsoft.EntityFrameworkCore;
using RadarChollos.Models;

namespace RadarChollos.Repositories
{
    public class AppDbContext : DbContext
    {
        public DbSet<Producto> Productos => Set<Producto>();
        public DbSet<Chollo> Chollos => Set<Chollo>();

        public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            modelBuilder.Entity<Chollo>(entity =>
            {
                entity.HasKey(c => c.Id);

                // El índice único evita que la misma oferta se guarde dos veces
                entity.HasIndex(c => c.Enlace)
                      .IsUnique();

                // Si se borra la regla los chollos quedan con ProductoId a null para no perder el historial
                entity.HasOne(c => c.Producto)
                      .WithMany()
                      .HasForeignKey(c => c.ProductoId)
                      .OnDelete(DeleteBehavior.SetNull);
            });

            modelBuilder.Entity<Producto>(entity =>
            {
                entity.HasKey(p => p.Id);
                entity.HasIndex(p => p.Patron);
            });
        }
    }
}
