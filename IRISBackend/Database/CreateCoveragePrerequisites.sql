/* Reviewed prerequisite for the existing EF migrations. Safe to rerun. */
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
SET XACT_ABORT ON;
BEGIN TRANSACTION;

IF OBJECT_ID(N'dbo.COVERAGE_ITEM', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.COVERAGE_ITEM
    (
        id_coverage_item int IDENTITY(1,1) NOT NULL CONSTRAINT PK_COVERAGE_ITEM PRIMARY KEY,
        coverage_level nvarchar(20) NOT NULL,
        coverage_code nvarchar(50) NOT NULL,
        coverage_name nvarchar(255) NOT NULL,
        description nvarchar(max) NULL,
        display_order int NULL,
        is_active bit NOT NULL CONSTRAINT DF_COVERAGE_ITEM_is_active DEFAULT (1),
        created_at datetime2 NOT NULL CONSTRAINT DF_COVERAGE_ITEM_created_at DEFAULT (sysutcdatetime()),
        created_by nvarchar(450) NOT NULL,
        updated_at datetime2 NULL,
        updated_by nvarchar(450) NULL,
        CONSTRAINT UQ_COVERAGE_ITEM_code UNIQUE (coverage_code),
        CONSTRAINT FK_COVERAGE_ITEM_created_by_USER FOREIGN KEY (created_by) REFERENCES dbo.AspNetUsers(Id),
        CONSTRAINT FK_COVERAGE_ITEM_updated_by_USER FOREIGN KEY (updated_by) REFERENCES dbo.AspNetUsers(Id)
    );
    CREATE INDEX IX_COVERAGE_ITEM_level_active ON dbo.COVERAGE_ITEM(coverage_level, is_active);
    CREATE INDEX IX_COVERAGE_ITEM_name ON dbo.COVERAGE_ITEM(coverage_name);
END;

IF OBJECT_ID(N'dbo.COVERAGE_RELATION', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.COVERAGE_RELATION
    (
        id_coverage_relation int IDENTITY(1,1) NOT NULL CONSTRAINT PK_COVERAGE_RELATION PRIMARY KEY,
        id_parent_coverage_item int NOT NULL,
        id_child_coverage_item int NOT NULL,
        relation_type nvarchar(20) NOT NULL,
        display_order int NULL,
        is_mandatory bit NOT NULL CONSTRAINT DF_COVERAGE_RELATION_is_mandatory DEFAULT (0),
        is_active bit NOT NULL CONSTRAINT DF_COVERAGE_RELATION_is_active DEFAULT (1),
        created_at datetime2 NOT NULL CONSTRAINT DF_COVERAGE_RELATION_created_at DEFAULT (sysutcdatetime()),
        created_by nvarchar(450) NOT NULL,
        updated_at datetime2 NULL,
        updated_by nvarchar(450) NULL,
        CONSTRAINT UQ_COVERAGE_RELATION_parent_child_type UNIQUE (id_parent_coverage_item, id_child_coverage_item, relation_type),
        CONSTRAINT FK_COVERAGE_RELATION_parent FOREIGN KEY (id_parent_coverage_item) REFERENCES dbo.COVERAGE_ITEM(id_coverage_item),
        CONSTRAINT FK_COVERAGE_RELATION_child FOREIGN KEY (id_child_coverage_item) REFERENCES dbo.COVERAGE_ITEM(id_coverage_item),
        CONSTRAINT FK_COVERAGE_RELATION_created_by_USER FOREIGN KEY (created_by) REFERENCES dbo.AspNetUsers(Id),
        CONSTRAINT FK_COVERAGE_RELATION_updated_by_USER FOREIGN KEY (updated_by) REFERENCES dbo.AspNetUsers(Id)
    );
    CREATE INDEX IX_COVERAGE_RELATION_child ON dbo.COVERAGE_RELATION(id_child_coverage_item, is_active);
    CREATE INDEX IX_COVERAGE_RELATION_parent ON dbo.COVERAGE_RELATION(id_parent_coverage_item, relation_type, is_active);
END;

IF OBJECT_ID(N'dbo.SLIP_TEMPLATE', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.SLIP_TEMPLATE
    (
        id_slip_template int IDENTITY(1,1) NOT NULL CONSTRAINT PK_SLIP_TEMPLATE PRIMARY KEY,
        id_coverage_item int NOT NULL,
        version_number int NOT NULL CONSTRAINT DF_SLIP_TEMPLATE_version_number DEFAULT (1),
        original_file_name nvarchar(260) NOT NULL,
        file_extension nvarchar(10) NOT NULL,
        mime_type nvarchar(100) NOT NULL,
        file_size_bytes bigint NOT NULL,
        file_content varbinary(max) NOT NULL,
        sha256_hash nchar(64) NULL,
        is_current bit NOT NULL CONSTRAINT DF_SLIP_TEMPLATE_is_current DEFAULT (1),
        is_active bit NOT NULL CONSTRAINT DF_SLIP_TEMPLATE_is_active DEFAULT (1),
        uploaded_at datetime2 NOT NULL CONSTRAINT DF_SLIP_TEMPLATE_uploaded_at DEFAULT (sysutcdatetime()),
        uploaded_by nvarchar(450) NOT NULL,
        CONSTRAINT UQ_SLIP_TEMPLATE_id_item UNIQUE (id_slip_template, id_coverage_item),
        CONSTRAINT UQ_SLIP_TEMPLATE_item_version UNIQUE (id_coverage_item, version_number),
        CONSTRAINT FK_SLIP_TEMPLATE_coverage_item FOREIGN KEY (id_coverage_item) REFERENCES dbo.COVERAGE_ITEM(id_coverage_item),
        CONSTRAINT FK_SLIP_TEMPLATE_uploaded_by_USER FOREIGN KEY (uploaded_by) REFERENCES dbo.AspNetUsers(Id)
    );
    CREATE UNIQUE INDEX UX_SLIP_TEMPLATE_current ON dbo.SLIP_TEMPLATE(id_coverage_item) WHERE is_current = 1 AND is_active = 1;
END;

IF OBJECT_ID(N'dbo.CONTRACT_COVERAGE_SELECTION', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.CONTRACT_COVERAGE_SELECTION
    (
        id_contract_coverage_selection int IDENTITY(1,1) NOT NULL CONSTRAINT PK_CONTRACT_COVERAGE_SELECTION PRIMARY KEY,
        id_contract int NOT NULL,
        id_coverage_item int NOT NULL,
        id_slip_template int NULL,
        created_at datetime2 NOT NULL CONSTRAINT DF_CONTRACT_COVERAGE_SELECTION_created_at DEFAULT (sysutcdatetime()),
        created_by nvarchar(450) NOT NULL,
        updated_at datetime2 NULL,
        updated_by nvarchar(450) NULL,
        CONSTRAINT UQ_CONTRACT_COVERAGE_SELECTION_contract_item UNIQUE (id_contract, id_coverage_item),
        CONSTRAINT FK_CONTRACT_COVERAGE_SELECTION_contract FOREIGN KEY (id_contract) REFERENCES dbo.CONTRACT(id_contract),
        CONSTRAINT FK_CONTRACT_COVERAGE_SELECTION_item FOREIGN KEY (id_coverage_item) REFERENCES dbo.COVERAGE_ITEM(id_coverage_item),
        CONSTRAINT FK_CONTRACT_COVERAGE_SELECTION_created_by_USER FOREIGN KEY (created_by) REFERENCES dbo.AspNetUsers(Id),
        CONSTRAINT FK_CONTRACT_COVERAGE_SELECTION_updated_by_USER FOREIGN KEY (updated_by) REFERENCES dbo.AspNetUsers(Id),
        CONSTRAINT FK_CONTRACT_COVERAGE_SELECTION_slip_item FOREIGN KEY (id_slip_template, id_coverage_item) REFERENCES dbo.SLIP_TEMPLATE(id_slip_template, id_coverage_item)
    );
    CREATE INDEX IX_CONTRACT_COVERAGE_SELECTION_contract ON dbo.CONTRACT_COVERAGE_SELECTION(id_contract);
END;

COMMIT TRANSACTION;
