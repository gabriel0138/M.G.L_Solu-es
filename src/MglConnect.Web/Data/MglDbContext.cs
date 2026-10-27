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

        public DbSet<Empresa> Empresas => Set<Empresa>();
        public DbSet<Usuario> Usuarios => Set<Usuario>();
        public DbSet<Cliente> Clientes => Set<Cliente>();
        public DbSet<Pedido> Pedidos => Set<Pedido>();
        public DbSet<Entrega> Entregas => Set<Entrega>();
        public DbSet<EventoRastreio> EventosRastreio => Set<EventoRastreio>();
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
