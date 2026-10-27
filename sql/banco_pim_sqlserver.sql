-- =============================================================================
-- PROJETO INTEGRADO MULTIDISCIPLINAR IV (PIM IV) - CST ADS - UNIP
-- PLATAFORMA M.G.L CONNECT: SOLUÇÃO TECNOLÓGICA INTEGRADA PARA GESTÃO LOGÍSTICA
-- SCRIPT DDL E DML PARA MICROSOFT SQL SERVER
-- =============================================================================

-- 1. CRIAÇÃO DAS TABELAS PRINCIPAIS (Capítulo 7.2 do PIM IV)

-- Tabela de Usuários (Administradores e Motoristas)
CREATE TABLE Usuarios (
    ID_Usuario INT IDENTITY(1,1) PRIMARY KEY,
    Nome VARCHAR(100) NOT NULL,
    Login VARCHAR(50) NOT NULL UNIQUE,
    SenhaHash VARCHAR(255) NOT NULL,
    Perfil VARCHAR(30) NOT NULL CHECK (Perfil IN ('Administrador', 'Motorista', 'Operador')),
    Telefone VARCHAR(20) NULL,
    Cpf VARCHAR(14) NULL,
    Ativo BIT NOT NULL DEFAULT 1,
    DataCadastro DATETIME NOT NULL DEFAULT GETDATE()
);
GO

-- Tabela de Clientes Corporativos
CREATE TABLE Clientes (
    ID_Cliente INT IDENTITY(1,1) PRIMARY KEY,
    RazaoSocial VARCHAR(150) NOT NULL,
    NomeFantasia VARCHAR(100) NULL,
    CNPJ VARCHAR(20) NOT NULL UNIQUE,
    Email VARCHAR(100) NULL,
    Telefone VARCHAR(20) NOT NULL,
    Cep VARCHAR(10) NULL,
    Logradouro VARCHAR(150) NULL,
    Numero VARCHAR(20) NULL,
    Complemento VARCHAR(100) NULL,
    Bairro VARCHAR(80) NULL,
    Cidade VARCHAR(80) NULL,
    Estado VARCHAR(2) NULL,
    Ativo BIT NOT NULL DEFAULT 1,
    DataCadastro DATETIME NOT NULL DEFAULT GETDATE()
);
GO

-- Tabela de Pedidos
CREATE TABLE Pedidos (
    ID_Pedido INT IDENTITY(1,1) PRIMARY KEY,
    NumeroPedido VARCHAR(30) NOT NULL UNIQUE,
    ID_Cliente INT NOT NULL,
    Descricao VARCHAR(255) NOT NULL,
    ValorTotal DECIMAL(18,2) NOT NULL DEFAULT 0.00,
    PesoKg DECIMAL(10,2) NOT NULL DEFAULT 1.0,
    DataPedido DATETIME NOT NULL DEFAULT GETDATE(),
    Status VARCHAR(30) NOT NULL DEFAULT 'Registrado',
    CONSTRAINT FK_Pedidos_Clientes FOREIGN KEY (ID_Cliente) 
        REFERENCES Clientes(ID_Cliente)
);
GO

-- Tabela de Entregas e Códigos de Rastreio (1 Pedido -> 0..1 Entrega)
CREATE TABLE Entregas (
    ID_Entrega INT IDENTITY(1,1) PRIMARY KEY,
    ID_Pedido INT NOT NULL UNIQUE, -- Restrição de unicidade exigida no item 7.2 do manual
    ID_Motorista INT NULL,
    CodigoRastreio VARCHAR(20) NOT NULL UNIQUE, -- Padrão Mercado Livre: MGL-XXXXXXXXBR
    DestinatarioNome VARCHAR(100) NOT NULL,
    DestinatarioDocumento VARCHAR(20) NULL,
    EnderecoDestino VARCHAR(200) NOT NULL,
    NumeroDestino VARCHAR(20) NULL,
    ComplementoDestino VARCHAR(100) NULL,
    BairroDestino VARCHAR(80) NULL,
    CidadeDestino VARCHAR(80) NOT NULL,
    EstadoDestino VARCHAR(2) NOT NULL,
    CepDestino VARCHAR(10) NOT NULL,
    StatusEntrega VARCHAR(30) NOT NULL DEFAULT 'Pendente' 
        CHECK (StatusEntrega IN ('Pendente', 'Em transporte', 'Entregue', 'Cancelada')),
    DataCriacao DATETIME NOT NULL DEFAULT GETDATE(),
    DataPrevisaoEntrega DATETIME NULL,
    DataEntregaRealizada DATETIME NULL,
    Observacoes VARCHAR(500) NULL,
    CONSTRAINT FK_Entregas_Pedidos FOREIGN KEY (ID_Pedido) 
        REFERENCES Pedidos(ID_Pedido),
    CONSTRAINT FK_Entregas_Usuarios FOREIGN KEY (ID_Motorista) 
        REFERENCES Usuarios(ID_Usuario) ON DELETE SET NULL
);
GO

-- Tabela de Checkpoints de Rastreamento (Linha do Tempo / Timeline Estilo Mercado Livre)
CREATE TABLE EventosRastreio (
    ID_Evento INT IDENTITY(1,1) PRIMARY KEY,
    ID_Entrega INT NOT NULL,
    DataHora DATETIME NOT NULL DEFAULT GETDATE(),
    Titulo VARCHAR(100) NOT NULL,
    Descricao VARCHAR(255) NULL,
    Localidade VARCHAR(100) NOT NULL,
    StatusAssociado VARCHAR(30) NOT NULL,
    Icone VARCHAR(50) NOT NULL DEFAULT 'bi-truck',
    CONSTRAINT FK_EventosRastreio_Entregas FOREIGN KEY (ID_Entrega) 
        REFERENCES Entregas(ID_Entrega) ON DELETE CASCADE
);
GO

-- 2. ÍNDICES DE OTIMIZAÇÃO (Capítulo 7.5 do PIM IV)
CREATE INDEX IX_Pedidos_ID_Cliente ON Pedidos(ID_Cliente);
CREATE INDEX IX_Entregas_ID_Motorista ON Entregas(ID_Motorista);
CREATE INDEX IX_Entregas_CodigoRastreio ON Entregas(CodigoRastreio);
CREATE INDEX IX_EventosRastreio_ID_Entrega ON EventosRastreio(ID_Entrega);
GO

-- 3. PROCEDIMENTO ARMAZENADO (Capítulo 7.3 do PIM IV)
-- Procedimento exigido: sp_ConsultarEntregasMotorista
-- Recebe o ID do motorista e retorna as entregas sob sua responsabilidade
CREATE OR ALTER PROCEDURE sp_ConsultarEntregasMotorista
    @ID_Motorista INT
AS
BEGIN
    SET NOCOUNT ON;

    SELECT 
        e.ID_Entrega,
        e.CodigoRastreio,
        p.NumeroPedido,
        c.RazaoSocial AS ClienteOrigem,
        e.DestinatarioNome,
        e.EnderecoDestino + ', ' + ISNULL(e.NumeroDestino, 'S/N') + ' - ' + e.CidadeDestino + '/' + e.EstadoDestino AS EnderecoCompleto,
        e.CepDestino,
        e.StatusEntrega,
        e.DataCriacao,
        e.DataPrevisaoEntrega
    FROM Entregas e
    INNER JOIN Pedidos p ON e.ID_Pedido = p.ID_Pedido
    INNER JOIN Clientes c ON p.ID_Cliente = c.ID_Cliente
    WHERE e.ID_Motorista = @ID_Motorista
    ORDER BY e.DataCriacao DESC;
END;
GO

-- 4. TRIGGER DE INTEGRIDADE (Capítulo 7.4 do PIM IV)
-- Trigger exigida: impedir que entregas com status 'Entregue' retornem aos status anteriores
CREATE OR ALTER TRIGGER trg_ImpedirAlteracaoAposEntregue
ON Entregas
AFTER UPDATE
AS
BEGIN
    SET NOCOUNT ON;

    IF EXISTS (
        SELECT 1 
        FROM deleted d
        INNER JOIN inserted i ON d.ID_Entrega = i.ID_Entrega
        WHERE d.StatusEntrega = 'Entregue' 
          AND i.StatusEntrega <> 'Entregue'
    )
    BEGIN
        RAISERROR ('Regra de Integridade Logística: Uma entrega que já consta como "Entregue" não pode ter seu status retroagido.', 16, 1);
        ROLLBACK TRANSACTION;
        RETURN;
    END;
END;
GO
