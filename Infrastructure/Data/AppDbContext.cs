using CoopagcuyApi.Common;
using CoopagcuyApi.Common.Auth;
using CoopagcuyApi.Features.Catalogos.Models;
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
    public DbSet<RegistroFaenamiento> Faenamientos => Set<RegistroFaenamiento>();
    public DbSet<Despacho> Despachos => Set<Despacho>();
    public DbSet<CodigoQR> CodigosQR => Set<CodigoQR>();
    public DbSet<Usuario> Usuarios => Set<Usuario>();
    public DbSet<Devolucion> Devoluciones => Set<Devolucion>();
    public DbSet<ProductoraCambio> ProductoraCambios => Set<ProductoraCambio>();
    public DbSet<Comunidad> Comunidades => Set<Comunidad>();
    public DbSet<Movilizacion> Movilizaciones => Set<Movilizacion>();
    public DbSet<Pago> Pagos => Set<Pago>();
    public DbSet<CuyRegistro> CuyRegistros => Set<CuyRegistro>();
    public DbSet<CuyFaenamiento> CuyFaenamientos => Set<CuyFaenamiento>();
    public DbSet<RetornoProductora> RetornosProductora => Set<RetornoProductora>();
    public DbSet<LoteFaenado> LotesFaenados => Set<LoteFaenado>();
    public DbSet<SyncEntregaProcesada> SyncEntregasProcesadas => Set<SyncEntregaProcesada>();
    public DbSet<DespachoCuy> DespachoCuys => Set<DespachoCuy>();

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

        // Lote (jaula multi-productora)
        modelBuilder.Entity<Lote>(e =>
        {
            e.HasKey(l => l.Id);
            e.HasIndex(l => l.CodigoLote).IsUnique();
            // Consulta frecuente: la jaula abierta de cada CAT
            e.HasIndex(l => new { l.CentroAcopio, l.Cerrado });
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

        // Faenamiento: un lote puede faenarse en varias sesiones parciales
        modelBuilder.Entity<RegistroFaenamiento>(e =>
        {
            e.HasKey(f => f.Id);
            e.Property(f => f.PesoTotalCanalGramos).HasPrecision(10, 2);
            e.Property(f => f.TemperaturaAlmacenamiento).HasPrecision(5, 2);
            e.Property(f => f.EstadoCanal).HasConversion<string>();
            e.HasOne(f => f.Lote)
             .WithMany(l => l.Faenamientos)
             .HasForeignKey(f => f.LoteId);
        });

        // Despacho: pertenece al lote faenado; LoteId queda como
        // referencia legada de los despachos previos al detalle por animal
        modelBuilder.Entity<Despacho>(e =>
        {
            e.HasKey(d => d.Id);
            e.Property(d => d.ClienteDestino).HasMaxLength(200).IsRequired();
            e.HasOne(d => d.LoteFaenado)
             .WithMany()
             .HasForeignKey(d => d.LoteFaenadoId);
            e.HasOne(d => d.Lote)
             .WithMany()
             .HasForeignKey(d => d.LoteId);
        });

        // Detalle por animal del despacho: un cuy faenado solo puede
        // despacharse una vez (índice único = garantía contra dobles
        // despachos incluso bajo concurrencia)
        modelBuilder.Entity<DespachoCuy>(e =>
        {
            e.HasKey(dc => dc.Id);
            e.HasIndex(dc => dc.CuyFaenamientoId).IsUnique();
            e.HasOne(dc => dc.Despacho)
             .WithMany(d => d.Cuyes)
             .HasForeignKey(dc => dc.DespachoId);
            e.HasOne(dc => dc.CuyFaenamiento)
             .WithMany()
             .HasForeignKey(dc => dc.CuyFaenamientoId);
        });

        // CodigoQR: del lote faenado (producto) o de la jaula (histórico)
        modelBuilder.Entity<CodigoQR>(e =>
        {
            e.HasKey(q => q.Id);
            e.Property(q => q.UrlPublica).HasMaxLength(500).IsRequired();
            e.HasOne(q => q.Lote)
             .WithOne(l => l.CodigoQR)
             .HasForeignKey<CodigoQR>(q => q.LoteId);
            e.HasOne(q => q.LoteFaenado)
             .WithMany()
             .HasForeignKey(q => q.LoteFaenadoId);
        });

        // Lote faenado: producto terminado con código propio (FAE-…)
        modelBuilder.Entity<LoteFaenado>(e =>
        {
            e.HasKey(lf => lf.Id);
            e.HasIndex(lf => lf.Codigo).IsUnique();
            e.Property(lf => lf.Codigo).HasMaxLength(20).IsRequired();
            e.Property(lf => lf.OperarioResponsable).HasMaxLength(150).IsRequired();
            e.Property(lf => lf.TemperaturaAlmacenamiento).HasPrecision(5, 2);
            e.Property(lf => lf.Observaciones).HasMaxLength(500);
            e.HasMany(lf => lf.Sesiones)
             .WithOne(f => f.LoteFaenado)
             .HasForeignKey(f => f.LoteFaenadoId);
        });

        // Usuario: la cédula es el identificador único de inicio de sesión;
        // el correo es solo un dato de contacto opcional (puede repetirse,
        // p. ej. un correo familiar compartido)
        modelBuilder.Entity<Usuario>(e =>
        {
            e.HasKey(u => u.Id);
            e.HasIndex(u => u.Cedula).IsUnique();
            e.Property(u => u.Cedula).HasMaxLength(10).IsRequired();
            e.Property(u => u.Email).HasMaxLength(200);
            e.Property(u => u.Rol).HasConversion<string>();
            e.Property(u => u.CatAsignado).HasConversion<string>();
        });

        // Devolución — RF-307: nace de un despacho; Lote y sesión quedan
        // como referencias legadas de devoluciones antiguas
        modelBuilder.Entity<Devolucion>(e =>
        {
            e.HasKey(d => d.Id);
            e.Property(d => d.ClienteDevuelve).HasMaxLength(200).IsRequired();
            e.Property(d => d.Motivo).HasMaxLength(500).IsRequired();
            e.Property(d => d.Responsable).HasMaxLength(150).IsRequired();
            e.Property(d => d.Observaciones).HasMaxLength(500);
            e.HasOne(d => d.Despacho)
             .WithMany()
             .HasForeignKey(d => d.DespachoId);
            e.HasOne(d => d.Lote)
             .WithMany()
             .HasForeignKey(d => d.LoteId);
            e.HasOne(d => d.RegistroFaenamiento)
             .WithMany()
             .HasForeignKey(d => d.RegistroFaenamientoId);
        });

        // Historial de cambios de productora — RF-105
        modelBuilder.Entity<ProductoraCambio>(e =>
        {
            e.HasKey(c => c.Id);
            e.Property(c => c.CampoModificado).HasMaxLength(50).IsRequired();
            e.Property(c => c.ValorAnterior).HasMaxLength(200);
            e.Property(c => c.ValorNuevo).HasMaxLength(200);
            e.Property(c => c.ModificadoPor).HasMaxLength(200).IsRequired();
            e.HasOne(c => c.Productora)
             .WithMany()
             .HasForeignKey(c => c.ProductoraId);
        });

        // Registro individual por cuy en recepción
        modelBuilder.Entity<CuyRegistro>(e =>
        {
            e.HasKey(c => c.Id);
            e.HasIndex(c => new { c.LoteId, c.NumeroEnLote }).IsUnique();
            e.Property(c => c.PesoGramos).HasPrecision(10, 2);
            e.Property(c => c.ColorPelaje).HasMaxLength(50).IsRequired();
            e.Property(c => c.EstadoOreja).HasMaxLength(50).IsRequired();
            e.Property(c => c.TamanoAnimal).HasMaxLength(50).IsRequired();
            e.Property(c => c.SignosClinicos).HasMaxLength(300);
            e.Property(c => c.Estado).HasConversion<string>();
            e.Property(c => c.MotivoNovedad).HasMaxLength(500);
            e.HasOne(c => c.Lote)
             .WithMany(l => l.Cuyes)
             .HasForeignKey(c => c.LoteId);
            e.HasOne(c => c.Productora)
             .WithMany()
             .HasForeignKey(c => c.ProductoraId);
        });

        // Estado individual por cuy en faenamiento
        modelBuilder.Entity<CuyFaenamiento>(e =>
        {
            e.HasKey(c => c.Id);
            e.HasIndex(c => new { c.RegistroFaenamientoId, c.NumeroEnLote }).IsUnique();
            e.Property(c => c.PesoCanalGramos).HasPrecision(10, 2);
            e.Property(c => c.Estado).HasConversion<string>();
            e.Property(c => c.Motivo).HasMaxLength(500);
            e.HasOne(c => c.Registro)
             .WithMany(f => f.Cuyes)
             .HasForeignKey(c => c.RegistroFaenamientoId);
        });

        // Retorno de cuy no apto a su productora de origen
        modelBuilder.Entity<RetornoProductora>(e =>
        {
            e.HasKey(r => r.Id);
            e.Property(r => r.Motivo).HasMaxLength(500).IsRequired();
            e.Property(r => r.Responsable).HasMaxLength(150).IsRequired();
            e.HasOne(r => r.Lote)
             .WithMany()
             .HasForeignKey(r => r.LoteId);
            e.HasOne(r => r.Productora)
             .WithMany()
             .HasForeignKey(r => r.ProductoraId)
             .OnDelete(DeleteBehavior.Restrict);
        });

        // Movilización CAT → planta (eslabón transporte)
        modelBuilder.Entity<Movilizacion>(e =>
        {
            e.HasKey(m => m.Id);
            e.HasIndex(m => m.LoteId).IsUnique(); // un lote viaja una sola vez
            e.Property(m => m.Conductor).HasMaxLength(150).IsRequired();
            e.Property(m => m.ResponsableDespacho).HasMaxLength(150).IsRequired();
            e.Property(m => m.CondicionesTransporte).HasMaxLength(300);
            e.Property(m => m.TipoForraje).HasMaxLength(200);
            e.Property(m => m.Observaciones).HasMaxLength(500);
            e.Property(m => m.RecibidoPor).HasMaxLength(150);
            e.Property(m => m.CondicionLlegada).HasMaxLength(300);
            e.HasOne(m => m.Lote)
             .WithOne(l => l.Movilizacion)
             .HasForeignKey<Movilizacion>(m => m.LoteId);
        });

        // Pago a productora (antes cuaderno manual)
        modelBuilder.Entity<Pago>(e =>
        {
            e.HasKey(p => p.Id);
            e.Property(p => p.MontoUsd).HasPrecision(10, 2);
            e.Property(p => p.MetodoPago).HasMaxLength(50).IsRequired();
            e.Property(p => p.Responsable).HasMaxLength(150).IsRequired();
            e.Property(p => p.Observaciones).HasMaxLength(500);
            e.HasOne(p => p.Productora)
             .WithMany()
             .HasForeignKey(p => p.ProductoraId);
            e.HasOne(p => p.Lote)
             .WithMany()
             .HasForeignKey(p => p.LoteId);
        });

        // Marca de idempotencia del sync offline — RF-211: la pareja
        // (dispositivo, id de cliente) solo puede procesarse una vez
        modelBuilder.Entity<SyncEntregaProcesada>(e =>
        {
            e.HasKey(s => s.Id);
            e.HasIndex(s => new { s.DispositivoId, s.IdCliente }).IsUnique();
            e.Property(s => s.DispositivoId).HasMaxLength(100).IsRequired();
            e.Property(s => s.IdCliente).HasMaxLength(100).IsRequired();
        });

        // Comunidad — catálogo gestionable RF-102 / RF-506
        modelBuilder.Entity<Comunidad>(e =>
        {
            e.HasKey(c => c.Id);
            e.HasIndex(c => c.Nombre).IsUnique();
            e.Property(c => c.Nombre).HasMaxLength(100).IsRequired();
            e.Property(c => c.Canton).HasMaxLength(100).IsRequired();
            e.Property(c => c.CatReferencia).HasConversion<string>();

            // Comunidades relevadas en el diagnóstico PRODUCTO1
            e.HasData(
                new Comunidad { Id = 1, Nombre = "Patococha", Canton = "Pucará", CatReferencia = CentroAcopio.PAT },
                new Comunidad { Id = 2, Nombre = "Las Nieves", Canton = "Nabón", CatReferencia = CentroAcopio.NIE },
                new Comunidad { Id = 3, Nombre = "Huertas", Canton = "Santa Isabel", CatReferencia = CentroAcopio.HUE },
                new Comunidad { Id = 4, Nombre = "Nabón / El Progreso", Canton = "Nabón", CatReferencia = CentroAcopio.NAB },
                new Comunidad { Id = 5, Nombre = "Pelincay", Canton = "Pucará", CatReferencia = CentroAcopio.PEL }
            );
        });
    }
}