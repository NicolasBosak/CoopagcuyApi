using CoopagcuyApi.Common;
using CoopagcuyApi.Common.Auth;
using CoopagcuyApi.Common.Auth.Recuperacion;
using CoopagcuyApi.Features.Catalogos.Models;
using CoopagcuyApi.Features.Faenamiento.Models;
using CoopagcuyApi.Features.Pagos.Models;
using CoopagcuyApi.Features.Productoras.Models;
using CoopagcuyApi.Features.QR.Models;
using CoopagcuyApi.Features.Recepcion.Models;
using CoopagcuyApi.Infrastructure.Data.Seed;
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
    public DbSet<DescuentoPago> Descuentos => Set<DescuentoPago>();
    public DbSet<CuyRegistro> CuyRegistros => Set<CuyRegistro>();
    public DbSet<CuyFaenamiento> CuyFaenamientos => Set<CuyFaenamiento>();
    public DbSet<RetornoProductora> RetornosProductora => Set<RetornoProductora>();
    public DbSet<LoteFaenado> LotesFaenados => Set<LoteFaenado>();
    public DbSet<SyncEntregaProcesada> SyncEntregasProcesadas => Set<SyncEntregaProcesada>();
    public DbSet<DespachoCuy> DespachoCuys => Set<DespachoCuy>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<EntregaPendienteVinculacion> EntregasPendientesVinculacion =>
        Set<EntregaPendienteVinculacion>();
    public DbSet<SolicitudRestablecerPassword> SolicitudesRestablecerPassword =>
        Set<SolicitudRestablecerPassword>();
    public DbSet<Provincia> Provincias => Set<Provincia>();
    public DbSet<Canton> Cantones => Set<Canton>();

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
            e.Property(p => p.CatAsignado).HasConversion<string>();

            // Restrict: una comunidad con productoras registradas no puede
            // borrarse. La baja se hace con Activa = false en el catálogo.
            e.HasOne(p => p.Comunidad)
             .WithMany()
             .HasForeignKey(p => p.ComunidadId)
             .OnDelete(DeleteBehavior.Restrict);

            // La comunidad viaja siempre con la productora. Son 5 filas y
            // prácticamente todo lector de Productora necesita su nombre;
            // sin esto, un Include olvidado en cualquiera de los ~17 sitios
            // que materializan productoras reventaría en tiempo de ejecución
            // al armar la ficha del QR o un PDF.
            e.Navigation(p => p.Comunidad).AutoInclude();
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
            e.Property(n => n.FotoUrl).HasMaxLength(500);
            e.HasOne(n => n.Lote)
             .WithMany(l => l.Novedades)
             .HasForeignKey(n => n.LoteId);

            // Explícita y en cascada, igual que la de Lote. Sin declararla,
            // EF elegiría ClientSetNull por ser una FK opcional y dejaría
            // filas huérfanas si algún día se borrara un cuy. Hoy no se
            // borran, así que la regla no se ejercita: se fija para que no
            // dependa de un valor por defecto que nadie eligió.
            e.HasOne(n => n.CuyRegistro)
             .WithMany()
             .HasForeignKey(n => n.CuyRegistroId)
             .OnDelete(DeleteBehavior.Cascade);
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
            e.Property(d => d.Chofer).HasMaxLength(150);
            e.Property(d => d.Ruta).HasMaxLength(200);
            e.Property(d => d.TipoMercado).HasMaxLength(20).IsRequired();
            e.Property(d => d.Ciudad).HasMaxLength(100);
            e.Property(d => d.Pais).HasMaxLength(100);
            e.Property(d => d.PrecioUnitarioUsd).HasPrecision(10, 2);
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

            // Restrict y no Cascade: un pago no se borra nunca en este
            // sistema, pero con Cascade borrarlo desmarcaría los animales en
            // silencio y el lote volvería a parecer entero.
            e.HasOne(c => c.VentaLocalPago)
             .WithMany()
             .HasForeignKey(c => c.VentaLocalPagoId)
             .OnDelete(DeleteBehavior.Restrict);

            // Se consulta en cada movilización y en cada listado de lotes:
            // "los cuyes de este lote que siguen disponibles".
            e.HasIndex(c => c.VentaLocalPagoId);
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
            // Siete claves del catálogo separadas por punto y coma caben de
            // sobra en 300; el mismo tamaño que ya tiene la frase compuesta.
            e.Property(m => m.CondicionesClaves).HasMaxLength(300);
            e.Property(m => m.CondicionesLlegadaClaves).HasMaxLength(300);
            e.HasOne(m => m.Lote)
             .WithOne(l => l.Movilizacion)
             .HasForeignKey<Movilizacion>(m => m.LoteId);
        });

        // Pago a productora (antes cuaderno manual)
        modelBuilder.Entity<Pago>(e =>
        {
            e.HasKey(p => p.Id);
            e.Property(p => p.MontoUsd).HasPrecision(10, 2);
            e.Property(p => p.ValorPorDia).HasPrecision(10, 2);
            e.Property(p => p.MontoPagadoUsd).HasPrecision(10, 2);
            e.Property(p => p.MetodoPago).HasMaxLength(50).IsRequired();
            e.Property(p => p.Responsable).HasMaxLength(150).IsRequired();
            e.Property(p => p.Observaciones).HasMaxLength(500);

            // Columna nueva NO anulable sobre una tabla con datos: el valor
            // por defecto va aquí y no solo en el inicializador de C#. EF no
            // lee el inicializador, y la migración saldría sin default
            // dejando indefinidas las filas que ya existen.
            e.Property(p => p.EsVentaLocal).HasDefaultValue(false);

            // Token de concurrencia optimista: dos /pagar simultáneos sobre el
            // MISMO ticket (con novedades distintas) pasan los dos el chequeo
            // "Estado == Pendiente" en memoria, y sin esto el UPDATE de abajo
            // es last-writer-wins — el perdedor pisa MontoPagadoUsd del
            // ganador mientras las filas de Descuentos de ambos ya quedaron
            // guardadas (el índice único solo bloquea repetir la MISMA
            // novedad). El monto persistido y la suma de sus propios
            // descuentos dejan de cuadrar. Con el Estado como token, EF
            // agrega el valor original a la cláusula WHERE del UPDATE: el
            // segundo en llegar afecta cero filas y RegistrarPagoEfectivoAsync
            // ya sabe convertir eso en 409 (DbUpdateConcurrencyException
            // hereda de DbUpdateException).
            e.Property(p => p.Estado).IsConcurrencyToken();

            e.HasOne(p => p.Productora)
             .WithMany()
             .HasForeignKey(p => p.ProductoraId);
            e.HasOne(p => p.Lote)
             .WithMany()
             .HasForeignKey(p => p.LoteId);
        });

        modelBuilder.Entity<DescuentoPago>()
            .Property(d => d.MontoUsd).HasPrecision(10, 2);

        // Un mismo defecto no se descuenta dos veces sobre el mismo ticket. Va en el
        // índice y no solo en el servicio: dos peticiones simultáneas pasarían las
        // dos por la validación y grabarían el descuento por duplicado.
        modelBuilder.Entity<DescuentoPago>()
            .HasIndex(d => new { d.PagoId, d.NovedadCatId })
            .IsUnique();

        // Restrict y no Cascade: borrar una novedad no puede llevarse por delante la
        // justificación de un pago ya cobrado.
        modelBuilder.Entity<DescuentoPago>()
            .HasOne(d => d.NovedadCat)
            .WithMany()
            .HasForeignKey(d => d.NovedadCatId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<DescuentoPago>()
            .HasOne(d => d.Pago)
            .WithMany(p => p.Descuentos)
            .HasForeignKey(d => d.PagoId)
            .OnDelete(DeleteBehavior.Cascade);

        // Marca de idempotencia del sync offline — RF-211: la pareja
        // (dispositivo, id de cliente) solo puede procesarse una vez
        modelBuilder.Entity<SyncEntregaProcesada>(e =>
        {
            e.HasKey(s => s.Id);
            e.HasIndex(s => new { s.DispositivoId, s.IdCliente }).IsUnique();
            e.Property(s => s.DispositivoId).HasMaxLength(100).IsRequired();
            e.Property(s => s.IdCliente).HasMaxLength(100).IsRequired();
        });

        // Refresh token / sesión persistente. Solo se guarda el HASH del
        // token (nunca el valor en claro), con índice único para la búsqueda
        // por hash en cada refresh.
        modelBuilder.Entity<RefreshToken>(e =>
        {
            e.HasKey(t => t.Id);
            e.HasIndex(t => t.TokenHash).IsUnique();
            e.HasIndex(t => t.UsuarioId);
            e.Property(t => t.TokenHash).HasMaxLength(64).IsRequired();
            e.Property(t => t.DispositivoId).HasMaxLength(100);
            e.Property(t => t.UserAgent).HasMaxLength(300);
            e.Property(t => t.IpCreacion).HasMaxLength(60);
            e.Property(t => t.ReemplazadoPorHash).HasMaxLength(64);
            e.Ignore(t => t.EstaActivo);
            e.HasOne(t => t.Usuario)
             .WithMany()
             .HasForeignKey(t => t.UsuarioId)
             .OnDelete(DeleteBehavior.Cascade);
        });

        // Entrega capturada offline cuya cédula es válida pero no coincide
        // con ninguna productora: queda en cuarentena hasta que un admin la
        // vincule desde la bandeja. El detalle de cuyes viaja como JSON.
        modelBuilder.Entity<EntregaPendienteVinculacion>(e =>
        {
            e.HasKey(v => v.Id);
            // La misma pareja (dispositivo, idCliente) no puede encolarse dos
            // veces: idempotencia del sync también para las pendientes.
            e.HasIndex(v => new { v.DispositivoId, v.IdCliente }).IsUnique();
            e.Property(v => v.Cedula).HasMaxLength(10).IsRequired();
            e.Property(v => v.CentroAcopio).HasConversion<string>();
            e.Property(v => v.ResponsableRecepcion).HasMaxLength(150).IsRequired();
            e.Property(v => v.Observaciones).HasMaxLength(500);
            e.Property(v => v.DispositivoId).HasMaxLength(100).IsRequired();
            e.Property(v => v.IdCliente).HasMaxLength(100).IsRequired();
            e.Property(v => v.CuyesJson).IsRequired();
            e.Property(v => v.Estado).HasConversion<string>();
        });

        // Solicitud de restablecimiento de contraseña: bandeja que atiende un
        // administrador. El índice único PARCIAL es la pieza importante —
        // garantiza en la base, no en código, que un usuario no acumule
        // solicitudes pendientes. El historial (Resuelta/Descartada) queda
        // fuera del filtro, así que el mismo usuario puede pedirlo otra vez
        // dentro de un mes sin chocar con su propia solicitud vieja.
        modelBuilder.Entity<SolicitudRestablecerPassword>(e =>
        {
            e.HasKey(s => s.Id);
            e.Property(s => s.CedulaSolicitada).HasMaxLength(10).IsRequired();
            e.Property(s => s.Estado).HasConversion<string>().HasMaxLength(20);
            // El valor por defecto se declara aquí y no solo en el
            // inicializador de la propiedad: EF no lo lee del C#, y sin esto
            // la migración añade la columna con cadena vacía — un valor que no
            // corresponde a ningún miembro del enum y que reventaría al leer
            // las solicitudes que ya existen.
            e.Property(s => s.Origen)
                .HasConversion<string>()
                .HasMaxLength(20)
                .HasDefaultValue(OrigenSolicitudPassword.Usuario);
            e.Property(s => s.ResueltaPor).HasMaxLength(10);
            e.Property(s => s.IpSolicitud).HasMaxLength(60);

            e.HasOne(s => s.Usuario)
                .WithMany()
                .HasForeignKey(s => s.UsuarioId)
                .OnDelete(DeleteBehavior.Cascade);

            // El Estado se persiste como texto (HasConversion<string>), por eso
            // el filtro compara contra 'Pendiente' y no contra un entero.
            e.HasIndex(s => s.UsuarioId)
                .IsUnique()
                .HasFilter("\"Estado\" = 'Pendiente'")
                .HasDatabaseName("IX_SolicitudesRestablecerPassword_Pendiente");
        });

        // Provincia — catálogo geográfico de primer nivel
        modelBuilder.Entity<Provincia>(e =>
        {
            e.HasKey(p => p.Id);
            e.HasIndex(p => p.Nombre).IsUnique();
            e.Property(p => p.Nombre).HasMaxLength(80).IsRequired();

            e.HasData(GeografiaEcuador.Provincias);
        });

        // Cantón — cuelga de una provincia. El nombre solo es único DENTRO de su
        // provincia: hay cantones homónimos en el Ecuador ("Bolívar" está en Carchi
        // y en Manabí, "Olmedo" en Loja y en Manabí).
        modelBuilder.Entity<Canton>(e =>
        {
            e.HasKey(c => c.Id);
            e.HasIndex(c => new { c.ProvinciaId, c.Nombre }).IsUnique();
            e.Property(c => c.Nombre).HasMaxLength(80).IsRequired();

            e.HasOne(c => c.Provincia)
                .WithMany(p => p.Cantones)
                .HasForeignKey(c => c.ProvinciaId)
                .OnDelete(DeleteBehavior.Restrict);

            e.HasData(GeografiaEcuador.Cantones);
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