using System;
using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using MglConnect.Web.Models;

namespace MglConnect.Web.Data
{
    public class MglDbContext : DbContext, IDataProtectionKeyContext
    {
        // TenantId injetado via ITenantProvider (ou fallback Guid.Empty para modo single-tenant)
        private readonly Guid _currentTenantId;
        private readonly bool _tenantIsBound;

        public MglDbContext(DbContextOptions<MglDbContext> options, ITenantProvider? tenantProvider = null)
            : base(options)
        {
            _currentTenantId = tenantProvider?.TenantId ?? Guid.Empty;
            _tenantIsBound = tenantProvider?.IsTenantBound ?? false;
        }

        public DbSet<PerfilViagemRevisao> PerfisViagemRevisoes => Set<PerfilViagemRevisao>();
        public DbSet<RotaAvaliacao> RotaAvaliacoes => Set<RotaAvaliacao>();
        public DbSet<Empresa> Empresas => Set<Empresa>();
        public DbSet<Usuario> Usuarios => Set<Usuario>();
        public DbSet<Cliente> Clientes => Set<Cliente>();
        public DbSet<Pedido> Pedidos => Set<Pedido>();
        public DbSet<Entrega> Entregas => Set<Entrega>();
        public DbSet<EventoRastreio> EventosRastreio => Set<EventoRastreio>();
        public DbSet<GpsArquivo> GpsArquivos => Set<GpsArquivo>();
        public DbSet<GpsEventoArquivado> GpsEventosArquivados => Set<GpsEventoArquivado>();
        public DbSet<PosicaoGps> PosicoesGps => Set<PosicaoGps>();
        public DbSet<Veiculo> Veiculos => Set<Veiculo>();
        public DbSet<CodigoTurnoMotorista> CodigosTurnoMotorista => Set<CodigoTurnoMotorista>();
        public DbSet<TurnoMotorista> TurnosMotorista => Set<TurnoMotorista>();
        public DbSet<AlertaEmergencia> AlertasEmergencia => Set<AlertaEmergencia>();
        public DbSet<AuditoriaOperacional> AuditoriaOperacional => Set<AuditoriaOperacional>();
        public DbSet<DataProtectionKey> DataProtectionKeys { get; set; } = null!;

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            // ══════════════════════════════════════════════════════════════════
            // GLOBAL QUERY FILTERS — Multi-Tenancy (SaaS B2B Pillar 1)
            // Una conta autenticada sem tenant válido só pode consultar registros
            // do tenant vazio; não deve desabilitar o isolamento globalmente.
            // ══════════════════════════════════════════════════════════════════
            modelBuilder.Entity<Empresa>()
                .HasQueryFilter(e => !_tenantIsBound || e.Id == _currentTenantId);

            modelBuilder.Entity<Pedido>()
                .HasIndex(p => new { p.TenantId, p.NumeroPedido })
                .IsUnique();
            modelBuilder.Entity<Pedido>()
                .HasIndex(p => p.ChaveIdempotencia)
                .IsUnique()
                .HasDatabaseName("IX_Pedidos_ChaveIdempotencia")
                .HasFilter(GetNullableUniqueFilter("ChaveIdempotencia"));
            modelBuilder.Entity<Pedido>()
                .Property(p => p.Versao)
                .IsConcurrencyToken();

            modelBuilder.Entity<Pedido>()
                .HasQueryFilter(p => !_tenantIsBound || p.TenantId == _currentTenantId);

            modelBuilder.Entity<Cliente>()
                .HasQueryFilter(c => !_tenantIsBound || c.TenantId == _currentTenantId);

            modelBuilder.Entity<Entrega>()
                .HasQueryFilter(e => !_tenantIsBound || e.TenantId == _currentTenantId);

            modelBuilder.Entity<EventoRastreio>()
                .HasQueryFilter(ev => !_tenantIsBound
                    || (ev.Entrega != null && ev.Entrega.TenantId == _currentTenantId));

            modelBuilder.Entity<Veiculo>()
                .HasQueryFilter(v => !_tenantIsBound || v.TenantId == _currentTenantId);

            modelBuilder.Entity<Usuario>()
                .HasQueryFilter(u => !_tenantIsBound || u.TenantId == _currentTenantId);

            // ══════════════════════════════════════════════════════════════════
            // ÍNDICES ÚNICOS
            // ══════════════════════════════════════════════════════════════════

            // Garantir restrição de unicidade: 1 Pedido pode ter no máximo 1 Entrega
            modelBuilder.Entity<Entrega>()
                .HasIndex(e => e.PedidoId)
                .IsUnique();
            modelBuilder.Entity<Entrega>()
                .HasIndex(e => e.MotoristaId)
                .IsUnique()
                .HasDatabaseName("IX_Entregas_AtribuicaoAtiva_MotoristaId")
                .HasFilter(GetActiveAssignmentFilter("MotoristaId"));
            modelBuilder.Entity<Entrega>()
                .HasIndex(e => e.VeiculoId)
                .IsUnique()
                .HasDatabaseName("IX_Entregas_AtribuicaoAtiva_VeiculoId")
                .HasFilter(GetActiveAssignmentFilter("VeiculoId"));
            modelBuilder.Entity<Entrega>()
                .Property(e => e.Versao)
                .IsConcurrencyToken();

            // Código de rastreio único para buscas rápidas e exclusividade
            modelBuilder.Entity<Entrega>()
                .HasIndex(e => e.CodigoRastreio)
                .IsUnique();

            modelBuilder.Entity<PosicaoGps>().HasQueryFilter(p => !_tenantIsBound || p.TenantId == _currentTenantId);
            modelBuilder.Entity<PosicaoGps>().HasIndex(p => p.EventoId).IsUnique();
            modelBuilder.Entity<PosicaoGps>().HasIndex(p => new { p.TenantId, p.EntregaId, p.CapturadoEmUtc, p.Id });
            modelBuilder.Entity<PosicaoGps>().HasOne<Entrega>().WithMany().HasForeignKey(p => p.EntregaId).OnDelete(DeleteBehavior.Restrict);
            modelBuilder.Entity<PosicaoGps>().HasOne<Usuario>().WithMany().HasForeignKey(p => p.MotoristaId).OnDelete(DeleteBehavior.Restrict);
            modelBuilder.Entity<PosicaoGps>().HasOne<Usuario>().WithMany().HasForeignKey(p => p.AtorUsuarioId).OnDelete(DeleteBehavior.Restrict);
            modelBuilder.Entity<PosicaoGps>().HasOne<Veiculo>().WithMany().HasForeignKey(p => p.VeiculoId).OnDelete(DeleteBehavior.Restrict);
            modelBuilder.Entity<PosicaoGps>().HasOne<TurnoMotorista>().WithMany().HasForeignKey(p => p.TurnoId).OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<PosicaoGps>().HasIndex(p => new { p.TenantId, p.EntregaId, p.OrdenadoEmUtc, p.Id });
            modelBuilder.Entity<PosicaoGps>().HasIndex(p => new { p.TenantId, p.EntregaId, p.RecebidoEmUtc });
            modelBuilder.Entity<PosicaoGps>().HasIndex(p => new { p.TenantId, p.RecebidoEmUtc });
            modelBuilder.Entity<GpsArquivo>().HasQueryFilter(p => !_tenantIsBound || p.TenantId == _currentTenantId);
            modelBuilder.Entity<GpsArquivo>().Property(p => p.Versao).IsConcurrencyToken();
            modelBuilder.Entity<GpsArquivo>().HasIndex(p => new { p.TenantId, p.EntregaId, p.CriadoEmUtc });
            modelBuilder.Entity<GpsArquivo>().HasOne<Entrega>().WithMany().HasForeignKey(p => p.EntregaId).OnDelete(DeleteBehavior.Restrict);
            modelBuilder.Entity<GpsArquivo>().HasOne<Usuario>().WithMany().HasForeignKey(p => p.CriadoPorUsuarioId).OnDelete(DeleteBehavior.Restrict);
            modelBuilder.Entity<GpsArquivo>().HasOne<Usuario>().WithMany().HasForeignKey(p => p.CompactadoPorUsuarioId).OnDelete(DeleteBehavior.Restrict);
            modelBuilder.Entity<GpsEventoArquivado>().HasQueryFilter(p => !_tenantIsBound || p.TenantId == _currentTenantId);
            modelBuilder.Entity<GpsEventoArquivado>().HasIndex(p => p.EventoId).IsUnique();
            modelBuilder.Entity<GpsEventoArquivado>().HasIndex(p => p.ArquivoId);
            modelBuilder.Entity<GpsEventoArquivado>().HasIndex(p => new { p.TenantId, p.EntregaId, p.CapturadoEmUtc });
            modelBuilder.Entity<GpsEventoArquivado>().HasIndex(p => new { p.TenantId, p.EntregaId, p.OrdenadoEmUtc, p.Id });
            modelBuilder.Entity<GpsEventoArquivado>().HasIndex(p => new { p.TenantId, p.EntregaId, p.RecebidoEmUtc });
            modelBuilder.Entity<GpsEventoArquivado>().HasIndex(p => new { p.TenantId, p.RecebidoEmUtc });
            modelBuilder.Entity<GpsEventoArquivado>().HasOne<GpsArquivo>().WithMany().HasForeignKey(p => p.ArquivoId).OnDelete(DeleteBehavior.Restrict);
            modelBuilder.Entity<GpsEventoArquivado>().HasOne<Entrega>().WithMany().HasForeignKey(p => p.EntregaId).OnDelete(DeleteBehavior.Restrict);
            modelBuilder.Entity<GpsEventoArquivado>().HasOne<Usuario>().WithMany().HasForeignKey(p => p.AtorUsuarioId).OnDelete(DeleteBehavior.Restrict);
            modelBuilder.Entity<GpsEventoArquivado>().HasOne<Usuario>().WithMany().HasForeignKey(p => p.MotoristaId).OnDelete(DeleteBehavior.Restrict);
            modelBuilder.Entity<GpsEventoArquivado>().HasOne<Veiculo>().WithMany().HasForeignKey(p => p.VeiculoId).OnDelete(DeleteBehavior.Restrict);
            modelBuilder.Entity<GpsEventoArquivado>().HasOne<TurnoMotorista>().WithMany().HasForeignKey(p => p.TurnoId).OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<PerfilViagemRevisao>().ToTable("PerfisViagemRevisoes");
            modelBuilder.Entity<PerfilViagemRevisao>().HasQueryFilter(a=>!_tenantIsBound||a.TenantId==_currentTenantId);
            modelBuilder.Entity<PerfilViagemRevisao>().HasIndex(a=>new{a.TenantId,a.EntregaId,a.Revisao}).IsUnique();
            modelBuilder.Entity<PerfilViagemRevisao>().HasOne<Entrega>().WithMany().HasForeignKey(a=>a.EntregaId).OnDelete(DeleteBehavior.Restrict);
            modelBuilder.Entity<PerfilViagemRevisao>().HasOne<Veiculo>().WithMany().HasForeignKey(a=>a.VeiculoId).OnDelete(DeleteBehavior.Restrict);
            modelBuilder.Entity<PerfilViagemRevisao>().HasOne<Usuario>().WithMany().HasForeignKey(a=>a.AtorUsuarioId).OnDelete(DeleteBehavior.Restrict);
            modelBuilder.Entity<Entrega>().Property(a=>a.PerfilViagemVersao).IsConcurrencyToken();
            modelBuilder.Entity<Entrega>().Property(a=>a.PerfilViagemJson).IsConcurrencyToken();
            modelBuilder.Entity<RotaAvaliacao>().ToTable("RotaAvaliacoes");
            modelBuilder.Entity<RotaAvaliacao>().HasQueryFilter(a=>!_tenantIsBound||a.TenantId==_currentTenantId);
            modelBuilder.Entity<RotaAvaliacao>().HasIndex(a=>new{a.TenantId,a.EntregaId,a.CriadoEmUtc});
            modelBuilder.Entity<RotaAvaliacao>().HasOne<Entrega>().WithMany().HasForeignKey(a=>a.EntregaId).OnDelete(DeleteBehavior.Restrict);
            modelBuilder.Entity<RotaAvaliacao>().HasOne<Veiculo>().WithMany().HasForeignKey(a=>a.VeiculoId).OnDelete(DeleteBehavior.Restrict);
            modelBuilder.Entity<RotaAvaliacao>().HasOne<Usuario>().WithMany().HasForeignKey(a=>a.AtorUsuarioId).OnDelete(DeleteBehavior.Restrict);
            modelBuilder.Entity<Veiculo>().Property(v=>v.PerfilOperacionalJson).IsConcurrencyToken();
            modelBuilder.Entity<Veiculo>().Property(v=>v.PerfilOperacionalVersao).IsConcurrencyToken();

            // Catalogue updates use existing values as concurrency tokens: no schema or history rewrite.
            foreach (var field in new[] { nameof(Veiculo.Ativo), nameof(Veiculo.Placa), nameof(Veiculo.Modelo),
                nameof(Veiculo.CapacidadeCargaKg), nameof(Veiculo.AlturaMetros), nameof(Veiculo.LarguraMetros),
                nameof(Veiculo.ComprimentoMetros), nameof(Veiculo.PesoToneladas), nameof(Veiculo.QtdEixos), nameof(Veiculo.TipoCarga) })
                modelBuilder.Entity<Veiculo>().Property(field).IsConcurrencyToken();
            foreach (var field in new[] { nameof(Usuario.Ativo), nameof(Usuario.Nome), nameof(Usuario.Login), nameof(Usuario.Telefone), nameof(Usuario.Perfil) })
                modelBuilder.Entity<Usuario>().Property(field).IsConcurrencyToken();

            // Login de usuário único
            modelBuilder.Entity<Usuario>()
                .HasIndex(u => u.Login)
                .IsUnique();

            // CNPJ do cliente único
            modelBuilder.Entity<Cliente>()
                .HasIndex(c => c.Cnpj)
                .IsUnique();

            // Placa do veículo única
            modelBuilder.Entity<Veiculo>()
                .HasIndex(v => v.Placa)
                .IsUnique();

            // Slug da empresa único
            modelBuilder.Entity<Empresa>()
                .HasIndex(e => e.Slug)
                .IsUnique();

            modelBuilder.Entity<CodigoTurnoMotorista>()
                .Property(c => c.Versao)
                .IsConcurrencyToken();
            modelBuilder.Entity<CodigoTurnoMotorista>()
                .HasIndex(c => c.EntregaId)
                .IsUnique()
                .HasDatabaseName("IX_CodigosTurnoMotorista_Ativo_EntregaId")
                .HasFilter(GetUnconsumedCodeFilter());
            modelBuilder.Entity<TurnoMotorista>()
                .HasIndex(t => t.MotoristaId)
                .IsUnique()
                .HasDatabaseName("IX_TurnosMotorista_Ativo_MotoristaId")
                .HasFilter(GetActiveShiftFilter("MotoristaId"));
            modelBuilder.Entity<TurnoMotorista>()
                .HasIndex(t => t.EntregaId)
                .IsUnique()
                .HasDatabaseName("IX_TurnosMotorista_Ativo_EntregaId")
                .HasFilter(GetActiveShiftFilter("EntregaId"));
            modelBuilder.Entity<TurnoMotorista>()
                .Property(t => t.Versao)
                .IsConcurrencyToken();
            modelBuilder.Entity<AlertaEmergencia>()
                .HasIndex(a => a.ChaveIdempotencia)
                .IsUnique()
                .HasDatabaseName("IX_AlertasEmergencia_ChaveIdempotencia");
            modelBuilder.Entity<AlertaEmergencia>()
                .Property(a => a.Versao)
                .IsConcurrencyToken();

            modelBuilder.Entity<CodigoTurnoMotorista>()
                .HasQueryFilter(c => !_tenantIsBound || c.TenantId == _currentTenantId);
            modelBuilder.Entity<TurnoMotorista>()
                .HasQueryFilter(t => !_tenantIsBound || t.TenantId == _currentTenantId);
            modelBuilder.Entity<AlertaEmergencia>()
                .HasQueryFilter(a => !_tenantIsBound || a.TenantId == _currentTenantId);
            modelBuilder.Entity<AuditoriaOperacional>()
                .HasQueryFilter(a => !_tenantIsBound || a.TenantId == _currentTenantId);
            modelBuilder.Entity<AuditoriaOperacional>()
                .HasIndex(a => new { a.TipoEntidade, a.IdentificadorEntidade, a.OcorridoEmUtc });

            // ══════════════════════════════════════════════════════════════════
            // RELACIONAMENTOS
            // ══════════════════════════════════════════════════════════════════

            // Operational history must not outlive its referenced records silently.
            // Restrict avoids cascading deletion of codes, shifts and emergencies.
            modelBuilder.Entity<CodigoTurnoMotorista>().HasOne<Entrega>().WithMany()
                .HasForeignKey(c => c.EntregaId).OnDelete(DeleteBehavior.Restrict);
            modelBuilder.Entity<CodigoTurnoMotorista>().HasOne<Usuario>().WithMany()
                .HasForeignKey(c => c.MotoristaId).OnDelete(DeleteBehavior.Restrict);
            modelBuilder.Entity<CodigoTurnoMotorista>().HasOne<Usuario>().WithMany()
                .HasForeignKey(c => c.EmitidoPorUsuarioId).OnDelete(DeleteBehavior.Restrict);
            modelBuilder.Entity<TurnoMotorista>().HasOne<Entrega>().WithMany()
                .HasForeignKey(t => t.EntregaId).OnDelete(DeleteBehavior.Restrict);
            modelBuilder.Entity<TurnoMotorista>().HasOne<Usuario>().WithMany()
                .HasForeignKey(t => t.MotoristaId).OnDelete(DeleteBehavior.Restrict);
            modelBuilder.Entity<TurnoMotorista>().HasOne<Veiculo>().WithMany()
                .HasForeignKey(t => t.VeiculoId).OnDelete(DeleteBehavior.Restrict);
            modelBuilder.Entity<TurnoMotorista>().HasOne<CodigoTurnoMotorista>().WithMany()
                .HasForeignKey(t => t.CodigoTurnoId).OnDelete(DeleteBehavior.Restrict);
            modelBuilder.Entity<AlertaEmergencia>().HasOne<Entrega>().WithMany()
                .HasForeignKey(a => a.EntregaId).OnDelete(DeleteBehavior.Restrict);
            modelBuilder.Entity<AlertaEmergencia>().HasOne<Usuario>().WithMany()
                .HasForeignKey(a => a.MotoristaId).OnDelete(DeleteBehavior.Restrict);
            modelBuilder.Entity<AlertaEmergencia>().HasOne<Veiculo>().WithMany()
                .HasForeignKey(a => a.VeiculoId).OnDelete(DeleteBehavior.Restrict);
            modelBuilder.Entity<AlertaEmergencia>().HasOne<Usuario>().WithMany()
                .HasForeignKey(a => a.ReconhecidoPorUsuarioId).OnDelete(DeleteBehavior.Restrict);

            // Relacionamento Pedido -> Entrega (1:0..1)
            modelBuilder.Entity<Entrega>()
                .HasOne(e => e.Pedido)
                .WithOne(p => p.Entrega)
                .HasForeignKey<Entrega>(e => e.PedidoId)
                .OnDelete(DeleteBehavior.Restrict);

            // Relacionamento Motorista (Usuario) -> Entregas (1:N)
            modelBuilder.Entity<Entrega>()
                .HasOne(e => e.Motorista)
                .WithMany()
                .HasForeignKey(e => e.MotoristaId)
                .OnDelete(DeleteBehavior.SetNull);

            // Relacionamento Veiculo -> Entregas (1:N)
            modelBuilder.Entity<Entrega>()
                .HasOne(e => e.Veiculo)
                .WithMany()
                .HasForeignKey(e => e.VeiculoId)
                .OnDelete(DeleteBehavior.SetNull);

            // Relacionamento Entrega -> EventosRastreio (1:N cascata)
            modelBuilder.Entity<EventoRastreio>()
                .HasOne(ev => ev.Entrega)
                .WithMany(e => e.EventosRastreio)
                .HasForeignKey(ev => ev.EntregaId)
                .OnDelete(DeleteBehavior.Cascade);

            // Relacionamento Cliente -> Usuario (1:N para portal do cliente)
            modelBuilder.Entity<Usuario>()
                .HasOne(u => u.Cliente)
                .WithMany()
                .HasForeignKey(u => u.ClienteId)
                .OnDelete(DeleteBehavior.SetNull);
        }

        private string GetActiveAssignmentFilter(string column) =>
            Database.ProviderName?.Contains("SqlServer", StringComparison.Ordinal) == true
                ? $"[{column}] IS NOT NULL AND [AtribuicaoAtiva] = 1"
                : Database.ProviderName?.Contains("Npgsql", StringComparison.Ordinal) == true
                    ? $"\"{column}\" IS NOT NULL AND \"AtribuicaoAtiva\" = TRUE"
                    : $"\"{column}\" IS NOT NULL AND \"AtribuicaoAtiva\" = 1";

        private string GetActiveShiftFilter(string column) =>
            Database.ProviderName?.Contains("SqlServer", StringComparison.Ordinal) == true
                ? $"[Ativo] = 1"
                : Database.ProviderName?.Contains("Npgsql", StringComparison.Ordinal) == true
                    ? "\"Ativo\" = TRUE"
                    : "\"Ativo\" = 1";

        private string GetUnconsumedCodeFilter() =>
            Database.ProviderName?.Contains("SqlServer", StringComparison.Ordinal) == true
                ? "[UsadoEmUtc] IS NULL AND [CanceladoEmUtc] IS NULL"
                : "\"UsadoEmUtc\" IS NULL AND \"CanceladoEmUtc\" IS NULL";

        private string GetNullableUniqueFilter(string column) =>
            Database.ProviderName?.Contains("SqlServer", StringComparison.Ordinal) == true
                ? $"[{column}] IS NOT NULL"
                : $"\"{column}\" IS NOT NULL";

        public override int SaveChanges()
        {
            ApplyTenantOwnership();
            return base.SaveChanges();
        }

        public override int SaveChanges(bool acceptAllChangesOnSuccess)
        {
            ApplyTenantOwnership();
            return base.SaveChanges(acceptAllChangesOnSuccess);
        }

        public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            ApplyTenantOwnership();
            return base.SaveChangesAsync(cancellationToken);
        }

        public override Task<int> SaveChangesAsync(
            bool acceptAllChangesOnSuccess,
            CancellationToken cancellationToken = default)
        {
            ApplyTenantOwnership();
            return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
        }

        private void ApplyTenantOwnership()
        {
            if(ChangeTracker.Entries<PerfilViagemRevisao>().Any(e=>e.State is EntityState.Modified or EntityState.Deleted))
                throw new InvalidOperationException("Revisões de perfil são imutáveis; crie uma nova declaração.");
            foreach (var entry in ChangeTracker.Entries<PosicaoGps>().Where(e => e.State == EntityState.Added))
                entry.Entity.OrdenadoEmUtc ??= entry.Entity.CapturadoEmUtc ?? entry.Entity.RecebidoEmUtc;

            if (!_tenantIsBound)
            {
                return;
            }

            foreach (var entry in ChangeTracker.Entries<ITenantOwned>())
            {
                if (entry.State == EntityState.Added)
                {
                    entry.Entity.TenantId = _currentTenantId;
                }
                else if ((entry.State is EntityState.Modified or EntityState.Deleted)
                         && (entry.Entity.TenantId != _currentTenantId
                             || entry.Property(nameof(ITenantOwned.TenantId)).OriginalValue is not Guid originalTenantId
                             || originalTenantId != _currentTenantId))
                {
                    throw new InvalidOperationException("A entidade não pertence ao tenant autenticado.");
                }
            }
        }
    }

    // ══════════════════════════════════════════════════════════════════
    // INTERFACE TENANT PROVIDER — SaaS Multi-Tenancy
    // ══════════════════════════════════════════════════════════════════
    public interface ITenantProvider
    {
        Guid TenantId { get; }
        bool IsTenantBound { get; }
    }

    /// <summary>
    /// Implementação padrão: resolve o TenantId via claim do usuário logado (HttpContext).
    /// Em single-tenant/demo, retorna Guid.Empty (sem filtro).
    /// </summary>
    public class HttpContextTenantProvider : ITenantProvider
    {
        private readonly Guid _tenantId;
        private readonly bool _isTenantBound;

        public HttpContextTenantProvider(Microsoft.AspNetCore.Http.IHttpContextAccessor httpContextAccessor)
        {
            var user = httpContextAccessor.HttpContext?.User;
            _isTenantBound = user?.Identity?.IsAuthenticated == true;
            var claim = user?.FindFirst("TenantId")?.Value;
            _tenantId = Guid.TryParse(claim, out var tid) ? tid : Guid.Empty;
        }

        public Guid TenantId => _tenantId;
        public bool IsTenantBound => _isTenantBound;
    }
}
