using CoopagcuyApi.Common.Auth;
using CoopagcuyApi.Features.Faenamiento.Models;
using CoopagcuyApi.Features.Productoras.Models;
using CoopagcuyApi.Features.QR.Models;
using CoopagcuyApi.Features.Recepcion.Models;
using Microsoft.EntityFrameworkCore;

namespace CoopagcuyApi.Infrastructure.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<Productora> Productoras => Set<Productora>();
    public DbSet<Lote> Lotes => Set<Lote>();
    public DbSet<Novedad> Novedades => Set<Novedad>();
    public DbSet<Faenamiento> Faenamientos => Set<Faenamiento>();
    public DbSet<Despacho> Despachos => Set<Despacho>();
    public DbSet<CodigoQR> CodigosQR => Set<CodigoQR>();
    public DbSet<Usuario> Usuarios => Set<Usuario>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Productora
        modelBuilder.Entity<Productora>(e =>
        {
            e.HasKey(p => p.Id);
            e.HasIndex(p => p.Cedula).IsUnique();
            e.Property(p => p.NombreCompleto).HasMaxLength(150).IsRequired();
            e.Property(p => p.Cedula).HasMaxLength(13).IsRequired();
            e.Property(p => p.Comunidad).HasMaxLength(100).IsRequired();
            e.Property(p => p.Canton).HasMaxLength(100).IsRequired();
            e.Property(p => p.CatAsignado).HasConversion<string>();
        });

        // Lote
        modelBuilder.Entity<Lote>(e =>
        {
            e.HasKey(l => l.Id);
            e.HasIndex(l => l.CodigoLote).IsUnique();
            e.Property(l => l.CodigoLote).HasMaxLength(20).IsRequired();
            e.Property(l => l.PesoTotalGramos).HasPrecision(10, 2);
            e.Property(l => l.Estado).HasConversion<string>();
            e.Property(l => l.CentroAcopio).HasConversion<string>();
            e.HasOne(l => l.Productora)
             .WithMany(p => p.Lotes)
             .HasForeignKey(l => l.ProductoraId);
        });

        // Novedad
        modelBuilder.Entity<Novedad>(e =>
        {
            e.HasKey(n => n.Id);
            e.Property(n => n.Tipo).HasConversion<string>();
            e.Property(n => n.Descripcion).HasMaxLength(500);
            e.Property(n => n.PesoRegistradoGramos).HasPrecision(10, 2);
            e.HasOne(n => n.Lote)
             .WithMany(l => l.Novedades)
             .HasForeignKey(n => n.LoteId);
        });

        // Faenamiento
        modelBuilder.Entity<Faenamiento>(e =>
        {
            e.HasKey(f => f.Id);
            e.Property(f => f.PesoTotalCanalGramos).HasPrecision(10, 2);
            e.Property(f => f.TemperaturaAlmacenamiento).HasPrecision(5, 2);
            e.Property(f => f.EstadoCanal).HasConversion<string>();
            e.HasOne(f => f.Lote)
             .WithOne(l => l.Faenamiento)
             .HasForeignKey<Faenamiento>(f => f.LoteId);
        });

        // Despacho
        modelBuilder.Entity<Despacho>(e =>
        {
            e.HasKey(d => d.Id);
            e.Property(d => d.ClienteDestino).HasMaxLength(200).IsRequired();
            e.HasOne(d => d.Lote)
             .WithMany()
             .HasForeignKey(d => d.LoteId);
        });

        // CodigoQR
        modelBuilder.Entity<CodigoQR>(e =>
        {
            e.HasKey(q => q.Id);
            e.Property(q => q.UrlPublica).HasMaxLength(500).IsRequired();
            e.HasOne(q => q.Lote)
             .WithOne(l => l.CodigoQR)
             .HasForeignKey<CodigoQR>(q => q.LoteId);
        });

        // Usuario
        modelBuilder.Entity<Usuario>(e =>
        {
            e.HasKey(u => u.Id);
            e.HasIndex(u => u.Email).IsUnique();
            e.Property(u => u.Email).HasMaxLength(200).IsRequired();
            e.Property(u => u.Rol).HasConversion<string>();
        });
    }
}