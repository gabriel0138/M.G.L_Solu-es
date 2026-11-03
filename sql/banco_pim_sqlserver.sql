-- M.G.L CONNECT — PIM IV — SNAPSHOT ACADEMICO 8B (30/10/2026)
-- Gerado do modelo EF Core atual: 17 tabelas, 37 FKs, 59 indices.
-- NAO EXECUTADO NO SQL SERVER nesta etapa. Nao e migracao nem parte do startup.
-- Somente banco de ensaio NOVO, VAZIO, criado separadamente pelo operador.
-- O nome deve iniciar por MglPim8B_. Nao inclui USE/CREATE DATABASE/DROP/limpeza.
-- Original pre-8B preservado em sql/historico/banco_pim_sqlserver_pre8b.sql.
-- Filtros TenantId da aplicacao nao sao RLS: acesso SQL direto requer permissoes restritas.
IF DB_NAME() NOT LIKE N'MglPim8B[_]%'
    THROW 51000, 'Use somente um banco novo de ensaio com prefixo MglPim8B_.', 1;
IF EXISTS (SELECT 1 FROM sys.tables WHERE is_ms_shipped = 0)
    THROW 51001, 'Banco nao vazio: este snapshot nao pode atualizar nem sobrescrever estruturas.', 1;
GO
CREATE TABLE [AuditoriaOperacional] (
    [Id] bigint NOT NULL IDENTITY,
    [TenantId] uniqueidentifier NOT NULL,
    [AtorUsuarioId] int NOT NULL,
    [Evento] nvarchar(60) NOT NULL,
    [TipoEntidade] nvarchar(40) NOT NULL,
    [IdentificadorEntidade] nvarchar(80) NOT NULL,
    [Detalhes] nvarchar(200) NULL,
    [OcorridoEmUtc] datetime2 NOT NULL,
    CONSTRAINT [PK_AuditoriaOperacional] PRIMARY KEY ([Id])
);
GO


CREATE TABLE [Clientes] (
    [Id] int NOT NULL IDENTITY,
    [TenantId] uniqueidentifier NOT NULL,
    [RazaoSocial] nvarchar(150) NOT NULL,
    [NomeFantasia] nvarchar(100) NULL,
    [Cnpj] nvarchar(20) NOT NULL,
    [Email] nvarchar(100) NULL,
    [Telefone] nvarchar(20) NOT NULL,
    [Cep] nvarchar(10) NULL,
    [Logradouro] nvarchar(150) NULL,
    [Numero] nvarchar(20) NULL,
    [Complemento] nvarchar(100) NULL,
    [Bairro] nvarchar(80) NULL,
    [Cidade] nvarchar(80) NULL,
    [Estado] nvarchar(2) NULL,
    [Ativo] bit NOT NULL,
    [DataCadastro] datetime2 NOT NULL,
    CONSTRAINT [PK_Clientes] PRIMARY KEY ([Id])
);
GO


CREATE TABLE [DataProtectionKeys] (
    [Id] int NOT NULL IDENTITY,
    [FriendlyName] nvarchar(max) NULL,
    [Xml] nvarchar(max) NULL,
    CONSTRAINT [PK_DataProtectionKeys] PRIMARY KEY ([Id])
);
GO


CREATE TABLE [Empresas] (
    [Id] uniqueidentifier NOT NULL,
    [Nome] nvarchar(150) NOT NULL,
    [Cnpj] nvarchar(20) NOT NULL,
    [Slug] nvarchar(50) NOT NULL,
    [PlanoAssinatura] nvarchar(50) NOT NULL,
    [Ativo] bit NOT NULL,
    [DataCriacao] datetime2 NOT NULL,
    CONSTRAINT [PK_Empresas] PRIMARY KEY ([Id])
);
GO


CREATE TABLE [Veiculos] (
    [Id] int NOT NULL IDENTITY,
    [TenantId] uniqueidentifier NOT NULL,
    [Placa] nvarchar(10) NOT NULL,
    [Modelo] nvarchar(80) NOT NULL,
    [CapacidadeCargaKg] decimal(10,2) NOT NULL,
    [AlturaMetros] decimal(5,2) NOT NULL,
    [LarguraMetros] decimal(5,2) NOT NULL,
    [ComprimentoMetros] decimal(5,2) NOT NULL,
    [PesoToneladas] decimal(6,2) NOT NULL,
    [QtdEixos] int NOT NULL,
    [TipoCarga] nvarchar(50) NOT NULL,
    [PerfilOperacionalJson] nvarchar(max) NULL,
    [PerfilOperacionalVersao] int NOT NULL,
    [Ativo] bit NOT NULL,
    CONSTRAINT [PK_Veiculos] PRIMARY KEY ([Id])
);
GO


CREATE TABLE [Pedidos] (
    [Id] int NOT NULL IDENTITY,
    [TenantId] uniqueidentifier NOT NULL,
    [NumeroPedido] nvarchar(30) NOT NULL,
    [ChaveIdempotencia] nvarchar(36) NULL,
    [Versao] int NOT NULL,
    [ClienteId] int NOT NULL,
    [Descricao] nvarchar(255) NOT NULL,
    [OrigemEndereco] nvarchar(200) NULL,
    [OrigemNumero] nvarchar(20) NULL,
    [OrigemBairro] nvarchar(80) NULL,
    [OrigemCidade] nvarchar(80) NULL,
    [OrigemEstado] nvarchar(2) NULL,
    [OrigemCep] nvarchar(10) NULL,
    [DestinoEndereco] nvarchar(200) NULL,
    [DestinoNumero] nvarchar(20) NULL,
    [DestinoBairro] nvarchar(80) NULL,
    [DestinoCidade] nvarchar(80) NULL,
    [DestinoEstado] nvarchar(2) NULL,
    [DestinoCep] nvarchar(10) NULL,
    [AlturaCargaMetros] decimal(5,2) NULL,
    [LarguraCargaMetros] decimal(5,2) NULL,
    [ComprimentoCargaMetros] decimal(5,2) NULL,
    [TipoCarga] nvarchar(50) NOT NULL,
    [ValorTotal] decimal(18,2) NOT NULL,
    [PesoKg] decimal(10,2) NOT NULL,
    [DataPedido] datetime2 NOT NULL,
    [Status] nvarchar(30) NOT NULL,
    CONSTRAINT [PK_Pedidos] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_Pedidos_Clientes_ClienteId] FOREIGN KEY ([ClienteId]) REFERENCES [Clientes] ([Id]) ON DELETE CASCADE
);
GO


CREATE TABLE [Usuarios] (
    [Id] int NOT NULL IDENTITY,
    [TenantId] uniqueidentifier NOT NULL,
    [Nome] nvarchar(100) NOT NULL,
    [Login] nvarchar(50) NOT NULL,
    [SenhaHash] nvarchar(255) NOT NULL,
    [Perfil] nvarchar(30) NOT NULL,
    [Telefone] nvarchar(20) NULL,
    [Cpf] nvarchar(14) NULL,
    [DataNascimento] date NULL,
    [Ativo] bit NOT NULL,
    [DataCadastro] datetime2 NOT NULL,
    [ClienteId] int NULL,
    CONSTRAINT [PK_Usuarios] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_Usuarios_Clientes_ClienteId] FOREIGN KEY ([ClienteId]) REFERENCES [Clientes] ([Id]) ON DELETE SET NULL
);
GO


CREATE TABLE [Entregas] (
    [Id] int NOT NULL IDENTITY,
    [TenantId] uniqueidentifier NOT NULL,
    [RotaCalculoAtualId] uniqueidentifier NULL,
    [PedidoId] int NOT NULL,
    [MotoristaId] int NULL,
    [VeiculoId] int NULL,
    [CodigoRastreio] nvarchar(20) NOT NULL,
    [DestinatarioNome] nvarchar(100) NOT NULL,
    [DestinatarioDocumento] nvarchar(20) NULL,
    [EnderecoDestino] nvarchar(200) NOT NULL,
    [NumeroDestino] nvarchar(20) NULL,
    [ComplementoDestino] nvarchar(100) NULL,
    [BairroDestino] nvarchar(80) NULL,
    [CidadeDestino] nvarchar(80) NOT NULL,
    [EstadoDestino] nvarchar(2) NOT NULL,
    [CepDestino] nvarchar(10) NOT NULL,
    [StatusEntrega] nvarchar(30) NOT NULL,
    [AtribuicaoAtiva] bit NOT NULL,
    [Versao] int NOT NULL,
    [PerfilViagemJson] nvarchar(max) NULL,
    [PerfilViagemVersao] int NOT NULL,
    [UltimaLatitudeGps] float NULL,
    [UltimaLongitudeGps] float NULL,
    [UltimaPrecisaoGps] float NULL,
    [UltimaVelocidadeGps] float NULL,
    [UltimaDirecaoGps] float NULL,
    [UltimoGpsCapturadoEmUtc] datetime2 NULL,
    [UltimoGpsRecebidoEmUtc] datetime2 NULL,
    [DataCriacao] datetime2 NOT NULL,
    [DataPrevisaoEntrega] datetime2 NULL,
    [DataEntregaRealizada] datetime2 NULL,
    [Observacoes] nvarchar(500) NULL,
    [AlturaMetros] decimal(5,2) NOT NULL,
    [LarguraMetros] decimal(5,2) NOT NULL,
    [PesoToneladas] decimal(6,2) NOT NULL,
    [QtdEixos] int NOT NULL,
    [TipoCarga] nvarchar(50) NOT NULL,
    [RotaRestritaCaminhao] bit NOT NULL,
    [NomeRecebedor] nvarchar(100) NULL,
    [DocumentoRecebedor] nvarchar(30) NULL,
    [DataHoraRecebimento] datetime2 NULL,
    [AssinaturaDigitalBase64] nvarchar(max) NULL,
    [FotoComprovantePath] nvarchar(255) NULL,
    CONSTRAINT [PK_Entregas] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_Entregas_Pedidos_PedidoId] FOREIGN KEY ([PedidoId]) REFERENCES [Pedidos] ([Id]) ON DELETE NO ACTION,
    CONSTRAINT [FK_Entregas_Usuarios_MotoristaId] FOREIGN KEY ([MotoristaId]) REFERENCES [Usuarios] ([Id]) ON DELETE SET NULL,
    CONSTRAINT [FK_Entregas_Veiculos_VeiculoId] FOREIGN KEY ([VeiculoId]) REFERENCES [Veiculos] ([Id]) ON DELETE SET NULL
);
GO


CREATE TABLE [AlertasEmergencia] (
    [Id] uniqueidentifier NOT NULL,
    [TenantId] uniqueidentifier NOT NULL,
    [ChaveIdempotencia] uniqueidentifier NOT NULL,
    [EntregaId] int NOT NULL,
    [MotoristaId] int NOT NULL,
    [VeiculoId] int NULL,
    [CriadoEmUtc] datetime2 NOT NULL,
    [Status] nvarchar(30) NOT NULL,
    [SituacaoLocalizacao] nvarchar(30) NOT NULL,
    [Latitude] float NULL,
    [Longitude] float NULL,
    [PrecisaoMetros] float NULL,
    [LocalizacaoCapturadaEmUtc] datetime2 NULL,
    [ReconhecidoPorUsuarioId] int NULL,
    [ReconhecidoEmUtc] datetime2 NULL,
    [AtualizadoEmUtc] datetime2 NULL,
    [Versao] int NOT NULL,
    CONSTRAINT [PK_AlertasEmergencia] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_AlertasEmergencia_Entregas_EntregaId] FOREIGN KEY ([EntregaId]) REFERENCES [Entregas] ([Id]) ON DELETE NO ACTION,
    CONSTRAINT [FK_AlertasEmergencia_Usuarios_MotoristaId] FOREIGN KEY ([MotoristaId]) REFERENCES [Usuarios] ([Id]) ON DELETE NO ACTION,
    CONSTRAINT [FK_AlertasEmergencia_Usuarios_ReconhecidoPorUsuarioId] FOREIGN KEY ([ReconhecidoPorUsuarioId]) REFERENCES [Usuarios] ([Id]) ON DELETE NO ACTION,
    CONSTRAINT [FK_AlertasEmergencia_Veiculos_VeiculoId] FOREIGN KEY ([VeiculoId]) REFERENCES [Veiculos] ([Id]) ON DELETE NO ACTION
);
GO


CREATE TABLE [CodigosTurnoMotorista] (
    [Id] uniqueidentifier NOT NULL,
    [TenantId] uniqueidentifier NOT NULL,
    [EntregaId] int NOT NULL,
    [MotoristaId] int NOT NULL,
    [CodigoHash] nvarchar(64) NOT NULL,
    [EmitidoPorUsuarioId] int NOT NULL,
    [EmitidoEmUtc] datetime2 NOT NULL,
    [ExpiraEmUtc] datetime2 NOT NULL,
    [UsadoEmUtc] datetime2 NULL,
    [CanceladoEmUtc] datetime2 NULL,
    [Tentativas] int NOT NULL,
    [Versao] int NOT NULL,
    CONSTRAINT [PK_CodigosTurnoMotorista] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_CodigosTurnoMotorista_Entregas_EntregaId] FOREIGN KEY ([EntregaId]) REFERENCES [Entregas] ([Id]) ON DELETE NO ACTION,
    CONSTRAINT [FK_CodigosTurnoMotorista_Usuarios_EmitidoPorUsuarioId] FOREIGN KEY ([EmitidoPorUsuarioId]) REFERENCES [Usuarios] ([Id]) ON DELETE NO ACTION,
    CONSTRAINT [FK_CodigosTurnoMotorista_Usuarios_MotoristaId] FOREIGN KEY ([MotoristaId]) REFERENCES [Usuarios] ([Id]) ON DELETE NO ACTION
);
GO


CREATE TABLE [EventosRastreio] (
    [Id] int NOT NULL IDENTITY,
    [EntregaId] int NOT NULL,
    [DataHora] datetime2 NOT NULL,
    [Titulo] nvarchar(100) NOT NULL,
    [Descricao] nvarchar(255) NULL,
    [Localidade] nvarchar(100) NOT NULL,
    [StatusAssociado] nvarchar(30) NOT NULL,
    [Icone] nvarchar(50) NOT NULL,
    CONSTRAINT [PK_EventosRastreio] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_EventosRastreio_Entregas_EntregaId] FOREIGN KEY ([EntregaId]) REFERENCES [Entregas] ([Id]) ON DELETE CASCADE
);
GO


CREATE TABLE [GpsArquivos] (
    [Id] uniqueidentifier NOT NULL,
    [TenantId] uniqueidentifier NOT NULL,
    [EntregaId] int NOT NULL,
    [CriadoPorUsuarioId] int NOT NULL,
    [CriadoEmUtc] datetime2 NOT NULL,
    [CompactadoPorUsuarioId] int NULL,
    [CompactadoEmUtc] datetime2 NULL,
    [Quantidade] int NOT NULL,
    [PrimeiraSequencia] bigint NOT NULL,
    [UltimaSequencia] bigint NOT NULL,
    [Sha256] varchar(64) NOT NULL,
    [ConteudoGzip] varbinary(max) NOT NULL,
    [Versao] int NOT NULL,
    CONSTRAINT [PK_GpsArquivos] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_GpsArquivos_Entregas_EntregaId] FOREIGN KEY ([EntregaId]) REFERENCES [Entregas] ([Id]) ON DELETE NO ACTION,
    CONSTRAINT [FK_GpsArquivos_Usuarios_CompactadoPorUsuarioId] FOREIGN KEY ([CompactadoPorUsuarioId]) REFERENCES [Usuarios] ([Id]) ON DELETE NO ACTION,
    CONSTRAINT [FK_GpsArquivos_Usuarios_CriadoPorUsuarioId] FOREIGN KEY ([CriadoPorUsuarioId]) REFERENCES [Usuarios] ([Id]) ON DELETE NO ACTION
);
GO


CREATE TABLE [PerfisViagemRevisoes] (
    [Id] uniqueidentifier NOT NULL,
    [TenantId] uniqueidentifier NOT NULL,
    [EntregaId] int NOT NULL,
    [VeiculoId] int NOT NULL,
    [AtorUsuarioId] int NOT NULL,
    [Revisao] int NOT NULL,
    [CriadoEmUtc] datetime2 NOT NULL,
    [PerfilJson] nvarchar(max) NOT NULL,
    [CadastroVeiculoJson] nvarchar(max) NOT NULL,
    CONSTRAINT [PK_PerfisViagemRevisoes] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_PerfisViagemRevisoes_Entregas_EntregaId] FOREIGN KEY ([EntregaId]) REFERENCES [Entregas] ([Id]) ON DELETE NO ACTION,
    CONSTRAINT [FK_PerfisViagemRevisoes_Usuarios_AtorUsuarioId] FOREIGN KEY ([AtorUsuarioId]) REFERENCES [Usuarios] ([Id]) ON DELETE NO ACTION,
    CONSTRAINT [FK_PerfisViagemRevisoes_Veiculos_VeiculoId] FOREIGN KEY ([VeiculoId]) REFERENCES [Veiculos] ([Id]) ON DELETE NO ACTION
);
GO


CREATE TABLE [RotaAvaliacoes] (
    [Id] uniqueidentifier NOT NULL,
    [TenantId] uniqueidentifier NOT NULL,
    [EntregaId] int NOT NULL,
    [VeiculoId] int NULL,
    [AtorUsuarioId] int NULL,
    [VinculoHash] nvarchar(64) NOT NULL,
    [RegrasHash] nvarchar(64) NOT NULL,
    [CriadoEmUtc] datetime2 NOT NULL,
    [ExpiraEmUtc] datetime2 NOT NULL,
    [Obsoleta] bit NOT NULL,
    [ResultadoJson] nvarchar(max) NOT NULL,
    CONSTRAINT [PK_RotaAvaliacoes] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_RotaAvaliacoes_Entregas_EntregaId] FOREIGN KEY ([EntregaId]) REFERENCES [Entregas] ([Id]) ON DELETE NO ACTION,
    CONSTRAINT [FK_RotaAvaliacoes_Usuarios_AtorUsuarioId] FOREIGN KEY ([AtorUsuarioId]) REFERENCES [Usuarios] ([Id]) ON DELETE NO ACTION,
    CONSTRAINT [FK_RotaAvaliacoes_Veiculos_VeiculoId] FOREIGN KEY ([VeiculoId]) REFERENCES [Veiculos] ([Id]) ON DELETE NO ACTION
);
GO


CREATE TABLE [TurnosMotorista] (
    [Id] uniqueidentifier NOT NULL,
    [TenantId] uniqueidentifier NOT NULL,
    [EntregaId] int NOT NULL,
    [MotoristaId] int NOT NULL,
    [VeiculoId] int NOT NULL,
    [CodigoTurnoId] uniqueidentifier NOT NULL,
    [IniciadoEmUtc] datetime2 NOT NULL,
    [EncerradoEmUtc] datetime2 NULL,
    [Ativo] bit NOT NULL,
    [Versao] int NOT NULL,
    CONSTRAINT [PK_TurnosMotorista] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_TurnosMotorista_CodigosTurnoMotorista_CodigoTurnoId] FOREIGN KEY ([CodigoTurnoId]) REFERENCES [CodigosTurnoMotorista] ([Id]) ON DELETE NO ACTION,
    CONSTRAINT [FK_TurnosMotorista_Entregas_EntregaId] FOREIGN KEY ([EntregaId]) REFERENCES [Entregas] ([Id]) ON DELETE NO ACTION,
    CONSTRAINT [FK_TurnosMotorista_Usuarios_MotoristaId] FOREIGN KEY ([MotoristaId]) REFERENCES [Usuarios] ([Id]) ON DELETE NO ACTION,
    CONSTRAINT [FK_TurnosMotorista_Veiculos_VeiculoId] FOREIGN KEY ([VeiculoId]) REFERENCES [Veiculos] ([Id]) ON DELETE NO ACTION
);
GO


CREATE TABLE [GpsEventosArquivados] (
    [Id] bigint NOT NULL,
    [EventoId] uniqueidentifier NOT NULL,
    [TenantId] uniqueidentifier NOT NULL,
    [EntregaId] int NOT NULL,
    [MotoristaId] int NOT NULL,
    [VeiculoId] int NOT NULL,
    [AtorUsuarioId] int NOT NULL,
    [TurnoId] uniqueidentifier NULL,
    [ArquivoId] uniqueidentifier NOT NULL,
    [CapturadoEmUtc] datetime2 NULL,
    [RecebidoEmUtc] datetime2 NOT NULL,
    [OrdenadoEmUtc] datetime2 NOT NULL,
    [PrecisaoMetros] float NULL,
    [Origem] nvarchar(12) NOT NULL,
    [CapturaInformada] bit NOT NULL,
    [ForaDeOrdem] bit NULL,
    [ImpressaoRequisicao] varchar(64) NOT NULL,
    CONSTRAINT [PK_GpsEventosArquivados] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_GpsEventosArquivados_Entregas_EntregaId] FOREIGN KEY ([EntregaId]) REFERENCES [Entregas] ([Id]) ON DELETE NO ACTION,
    CONSTRAINT [FK_GpsEventosArquivados_GpsArquivos_ArquivoId] FOREIGN KEY ([ArquivoId]) REFERENCES [GpsArquivos] ([Id]) ON DELETE NO ACTION,
    CONSTRAINT [FK_GpsEventosArquivados_TurnosMotorista_TurnoId] FOREIGN KEY ([TurnoId]) REFERENCES [TurnosMotorista] ([Id]) ON DELETE NO ACTION,
    CONSTRAINT [FK_GpsEventosArquivados_Usuarios_AtorUsuarioId] FOREIGN KEY ([AtorUsuarioId]) REFERENCES [Usuarios] ([Id]) ON DELETE NO ACTION,
    CONSTRAINT [FK_GpsEventosArquivados_Usuarios_MotoristaId] FOREIGN KEY ([MotoristaId]) REFERENCES [Usuarios] ([Id]) ON DELETE NO ACTION,
    CONSTRAINT [FK_GpsEventosArquivados_Veiculos_VeiculoId] FOREIGN KEY ([VeiculoId]) REFERENCES [Veiculos] ([Id]) ON DELETE NO ACTION
);
GO


CREATE TABLE [PosicoesGps] (
    [Id] bigint NOT NULL IDENTITY,
    [EventoId] uniqueidentifier NOT NULL,
    [TenantId] uniqueidentifier NOT NULL,
    [EntregaId] int NOT NULL,
    [MotoristaId] int NOT NULL,
    [VeiculoId] int NOT NULL,
    [TurnoId] uniqueidentifier NULL,
    [AtorUsuarioId] int NOT NULL,
    [Latitude] float NOT NULL,
    [Longitude] float NOT NULL,
    [PrecisaoMetros] float NULL,
    [VelocidadeKmH] float NULL,
    [Direcao] float NULL,
    [CapturadoEmUtc] datetime2 NULL,
    [OrdenadoEmUtc] datetime2 NULL,
    [ForaDeOrdem] bit NULL,
    [RecebidoEmUtc] datetime2 NOT NULL,
    [Origem] nvarchar(12) NOT NULL,
    [CapturaInformada] bit NOT NULL,
    CONSTRAINT [PK_PosicoesGps] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_PosicoesGps_Entregas_EntregaId] FOREIGN KEY ([EntregaId]) REFERENCES [Entregas] ([Id]) ON DELETE NO ACTION,
    CONSTRAINT [FK_PosicoesGps_TurnosMotorista_TurnoId] FOREIGN KEY ([TurnoId]) REFERENCES [TurnosMotorista] ([Id]) ON DELETE NO ACTION,
    CONSTRAINT [FK_PosicoesGps_Usuarios_AtorUsuarioId] FOREIGN KEY ([AtorUsuarioId]) REFERENCES [Usuarios] ([Id]) ON DELETE NO ACTION,
    CONSTRAINT [FK_PosicoesGps_Usuarios_MotoristaId] FOREIGN KEY ([MotoristaId]) REFERENCES [Usuarios] ([Id]) ON DELETE NO ACTION,
    CONSTRAINT [FK_PosicoesGps_Veiculos_VeiculoId] FOREIGN KEY ([VeiculoId]) REFERENCES [Veiculos] ([Id]) ON DELETE NO ACTION
);
GO


CREATE UNIQUE INDEX [IX_AlertasEmergencia_ChaveIdempotencia] ON [AlertasEmergencia] ([ChaveIdempotencia]);
GO


CREATE INDEX [IX_AlertasEmergencia_EntregaId] ON [AlertasEmergencia] ([EntregaId]);
GO


CREATE INDEX [IX_AlertasEmergencia_MotoristaId] ON [AlertasEmergencia] ([MotoristaId]);
GO


CREATE INDEX [IX_AlertasEmergencia_ReconhecidoPorUsuarioId] ON [AlertasEmergencia] ([ReconhecidoPorUsuarioId]);
GO


CREATE INDEX [IX_AlertasEmergencia_VeiculoId] ON [AlertasEmergencia] ([VeiculoId]);
GO


CREATE INDEX [IX_AuditoriaOperacional_TipoEntidade_IdentificadorEntidade_OcorridoEmUtc] ON [AuditoriaOperacional] ([TipoEntidade], [IdentificadorEntidade], [OcorridoEmUtc]);
GO


CREATE UNIQUE INDEX [IX_Clientes_Cnpj] ON [Clientes] ([Cnpj]);
GO


CREATE UNIQUE INDEX [IX_CodigosTurnoMotorista_Ativo_EntregaId] ON [CodigosTurnoMotorista] ([EntregaId]) WHERE [UsadoEmUtc] IS NULL AND [CanceladoEmUtc] IS NULL;
GO


CREATE INDEX [IX_CodigosTurnoMotorista_EmitidoPorUsuarioId] ON [CodigosTurnoMotorista] ([EmitidoPorUsuarioId]);
GO


CREATE INDEX [IX_CodigosTurnoMotorista_MotoristaId] ON [CodigosTurnoMotorista] ([MotoristaId]);
GO


CREATE UNIQUE INDEX [IX_Empresas_Slug] ON [Empresas] ([Slug]);
GO


CREATE UNIQUE INDEX [IX_Entregas_AtribuicaoAtiva_MotoristaId] ON [Entregas] ([MotoristaId]) WHERE [MotoristaId] IS NOT NULL AND [AtribuicaoAtiva] = 1;
GO


CREATE UNIQUE INDEX [IX_Entregas_AtribuicaoAtiva_VeiculoId] ON [Entregas] ([VeiculoId]) WHERE [VeiculoId] IS NOT NULL AND [AtribuicaoAtiva] = 1;
GO


CREATE UNIQUE INDEX [IX_Entregas_CodigoRastreio] ON [Entregas] ([CodigoRastreio]);
GO


CREATE UNIQUE INDEX [IX_Entregas_PedidoId] ON [Entregas] ([PedidoId]);
GO


CREATE INDEX [IX_EventosRastreio_EntregaId] ON [EventosRastreio] ([EntregaId]);
GO


CREATE INDEX [IX_GpsArquivos_CompactadoPorUsuarioId] ON [GpsArquivos] ([CompactadoPorUsuarioId]);
GO


CREATE INDEX [IX_GpsArquivos_CriadoPorUsuarioId] ON [GpsArquivos] ([CriadoPorUsuarioId]);
GO


CREATE INDEX [IX_GpsArquivos_EntregaId] ON [GpsArquivos] ([EntregaId]);
GO


CREATE INDEX [IX_GpsArquivos_TenantId_EntregaId_CriadoEmUtc] ON [GpsArquivos] ([TenantId], [EntregaId], [CriadoEmUtc]);
GO


CREATE INDEX [IX_GpsEventosArquivados_ArquivoId] ON [GpsEventosArquivados] ([ArquivoId]);
GO


CREATE INDEX [IX_GpsEventosArquivados_AtorUsuarioId] ON [GpsEventosArquivados] ([AtorUsuarioId]);
GO


CREATE INDEX [IX_GpsEventosArquivados_EntregaId] ON [GpsEventosArquivados] ([EntregaId]);
GO


CREATE UNIQUE INDEX [IX_GpsEventosArquivados_EventoId] ON [GpsEventosArquivados] ([EventoId]);
GO


CREATE INDEX [IX_GpsEventosArquivados_MotoristaId] ON [GpsEventosArquivados] ([MotoristaId]);
GO


CREATE INDEX [IX_GpsEventosArquivados_TenantId_EntregaId_CapturadoEmUtc] ON [GpsEventosArquivados] ([TenantId], [EntregaId], [CapturadoEmUtc]);
GO


CREATE INDEX [IX_GpsEventosArquivados_TenantId_EntregaId_OrdenadoEmUtc_Id] ON [GpsEventosArquivados] ([TenantId], [EntregaId], [OrdenadoEmUtc], [Id]);
GO


CREATE INDEX [IX_GpsEventosArquivados_TenantId_EntregaId_RecebidoEmUtc] ON [GpsEventosArquivados] ([TenantId], [EntregaId], [RecebidoEmUtc]);
GO


CREATE INDEX [IX_GpsEventosArquivados_TenantId_RecebidoEmUtc] ON [GpsEventosArquivados] ([TenantId], [RecebidoEmUtc]);
GO


CREATE INDEX [IX_GpsEventosArquivados_TurnoId] ON [GpsEventosArquivados] ([TurnoId]);
GO


CREATE INDEX [IX_GpsEventosArquivados_VeiculoId] ON [GpsEventosArquivados] ([VeiculoId]);
GO


CREATE UNIQUE INDEX [IX_Pedidos_ChaveIdempotencia] ON [Pedidos] ([ChaveIdempotencia]) WHERE [ChaveIdempotencia] IS NOT NULL;
GO


CREATE INDEX [IX_Pedidos_ClienteId] ON [Pedidos] ([ClienteId]);
GO


CREATE UNIQUE INDEX [IX_Pedidos_TenantId_NumeroPedido] ON [Pedidos] ([TenantId], [NumeroPedido]);
GO


CREATE INDEX [IX_PerfisViagemRevisoes_AtorUsuarioId] ON [PerfisViagemRevisoes] ([AtorUsuarioId]);
GO


CREATE INDEX [IX_PerfisViagemRevisoes_EntregaId] ON [PerfisViagemRevisoes] ([EntregaId]);
GO


CREATE UNIQUE INDEX [IX_PerfisViagemRevisoes_TenantId_EntregaId_Revisao] ON [PerfisViagemRevisoes] ([TenantId], [EntregaId], [Revisao]);
GO


CREATE INDEX [IX_PerfisViagemRevisoes_VeiculoId] ON [PerfisViagemRevisoes] ([VeiculoId]);
GO


CREATE INDEX [IX_PosicoesGps_AtorUsuarioId] ON [PosicoesGps] ([AtorUsuarioId]);
GO


CREATE INDEX [IX_PosicoesGps_EntregaId] ON [PosicoesGps] ([EntregaId]);
GO


CREATE UNIQUE INDEX [IX_PosicoesGps_EventoId] ON [PosicoesGps] ([EventoId]);
GO


CREATE INDEX [IX_PosicoesGps_MotoristaId] ON [PosicoesGps] ([MotoristaId]);
GO


CREATE INDEX [IX_PosicoesGps_TenantId_EntregaId_CapturadoEmUtc_Id] ON [PosicoesGps] ([TenantId], [EntregaId], [CapturadoEmUtc], [Id]);
GO


CREATE INDEX [IX_PosicoesGps_TenantId_EntregaId_OrdenadoEmUtc_Id] ON [PosicoesGps] ([TenantId], [EntregaId], [OrdenadoEmUtc], [Id]);
GO


CREATE INDEX [IX_PosicoesGps_TenantId_EntregaId_RecebidoEmUtc] ON [PosicoesGps] ([TenantId], [EntregaId], [RecebidoEmUtc]);
GO


CREATE INDEX [IX_PosicoesGps_TenantId_RecebidoEmUtc] ON [PosicoesGps] ([TenantId], [RecebidoEmUtc]);
GO


CREATE INDEX [IX_PosicoesGps_TurnoId] ON [PosicoesGps] ([TurnoId]);
GO


CREATE INDEX [IX_PosicoesGps_VeiculoId] ON [PosicoesGps] ([VeiculoId]);
GO


CREATE INDEX [IX_RotaAvaliacoes_AtorUsuarioId] ON [RotaAvaliacoes] ([AtorUsuarioId]);
GO


CREATE INDEX [IX_RotaAvaliacoes_EntregaId] ON [RotaAvaliacoes] ([EntregaId]);
GO


CREATE INDEX [IX_RotaAvaliacoes_TenantId_EntregaId_CriadoEmUtc] ON [RotaAvaliacoes] ([TenantId], [EntregaId], [CriadoEmUtc]);
GO


CREATE INDEX [IX_RotaAvaliacoes_VeiculoId] ON [RotaAvaliacoes] ([VeiculoId]);
GO


CREATE UNIQUE INDEX [IX_TurnosMotorista_Ativo_EntregaId] ON [TurnosMotorista] ([EntregaId]) WHERE [Ativo] = 1;
GO


CREATE UNIQUE INDEX [IX_TurnosMotorista_Ativo_MotoristaId] ON [TurnosMotorista] ([MotoristaId]) WHERE [Ativo] = 1;
GO


CREATE INDEX [IX_TurnosMotorista_CodigoTurnoId] ON [TurnosMotorista] ([CodigoTurnoId]);
GO


CREATE INDEX [IX_TurnosMotorista_VeiculoId] ON [TurnosMotorista] ([VeiculoId]);
GO


CREATE INDEX [IX_Usuarios_ClienteId] ON [Usuarios] ([ClienteId]);
GO


CREATE UNIQUE INDEX [IX_Usuarios_Login] ON [Usuarios] ([Login]);
GO


CREATE UNIQUE INDEX [IX_Veiculos_Placa] ON [Veiculos] ([Placa]);
GO



-- ROTINAS ACADEMICAS: definidas, nao chamadas pela aplicacao EF e nao executadas nesta etapa.
-- A API deve fornecer @TenantId/@AtorUsuarioId a partir da identidade autenticada.
-- Estes parametros NAO autenticam um chamador SQL; nao liberar conexao/EXECUTE a clientes.
CREATE OR ALTER PROCEDURE dbo.sp_ConsultarEntregasMotorista
    @TenantId UNIQUEIDENTIFIER,
    @ID_Motorista INT,
    @AtorUsuarioId INT
AS
BEGIN
    SET NOCOUNT ON;
    IF NOT EXISTS (
        SELECT 1 FROM dbo.Usuarios u
        WHERE u.Id=@AtorUsuarioId AND u.TenantId=@TenantId AND u.Ativo=1
          AND (u.Perfil IN (N'Admin',N'Administrador') OR (u.Perfil=N'Motorista' AND u.Id=@ID_Motorista))
    ) THROW 51002, 'Escopo de consulta nao autorizado.', 1;
    IF NOT EXISTS (SELECT 1 FROM dbo.Usuarios u WHERE u.Id=@ID_Motorista AND u.TenantId=@TenantId AND u.Perfil=N'Motorista' AND u.Ativo=1)
        THROW 51003, 'Motorista indisponivel no escopo.', 1;
    SELECT e.Id, e.StatusEntrega, e.CidadeDestino, e.EstadoDestino,
           v.Modelo AS Veiculo, v.Placa,
           CAST(CASE WHEN EXISTS (
               SELECT 1 FROM dbo.TurnosMotorista t
               WHERE t.EntregaId=e.Id AND t.MotoristaId=@ID_Motorista AND t.TenantId=@TenantId AND t.Ativo=1
           ) THEN 1 ELSE 0 END AS BIT) AS TurnoAtivo
    FROM dbo.Entregas e
    INNER JOIN dbo.Pedidos p ON p.Id=e.PedidoId AND p.TenantId=@TenantId
    LEFT JOIN dbo.Veiculos v ON v.Id=e.VeiculoId AND v.TenantId=@TenantId
    WHERE e.TenantId=@TenantId AND e.MotoristaId=@ID_Motorista AND e.AtribuicaoAtiva=1
      AND e.StatusEntrega NOT IN (N'Entregue',N'Cancelada')
    ORDER BY e.DataCriacao;
END;
GO
CREATE OR ALTER TRIGGER dbo.trg_ImpedirAlteracaoAposEntregue
ON dbo.Entregas
AFTER UPDATE
AS
BEGIN
    SET NOCOUNT ON;
    -- Operacao por conjunto: inclui UPDATE de varias linhas, sem variavel escalar de inserted.
    IF EXISTS (
        SELECT 1 FROM deleted d INNER JOIN inserted i ON i.Id=d.Id
        WHERE d.StatusEntrega=N'Entregue' AND i.StatusEntrega<>N'Entregue'
    ) THROW 51004, 'Entrega concluida nao pode ter o status retroagido.', 1;
END;
GO
-- Nao ha DML de demonstracao aqui. Testar as rotinas somente em fixture isolada.
