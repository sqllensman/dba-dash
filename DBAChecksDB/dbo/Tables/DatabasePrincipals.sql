﻿CREATE TABLE [dbo].[DatabasePrincipals] (
    [DatabaseID]                          INT            NOT NULL,
    [name]                                NVARCHAR (128) NOT NULL,
    [principal_id]                        INT            NOT NULL,
    [type]                                CHAR (1)       NOT NULL,
    [type_desc]                           NVARCHAR (60)  NOT NULL,
    [default_schema_name]                 NVARCHAR (128) NULL,
    [create_date]                         DATETIME       NOT NULL,
    [modify_date]                         DATETIME       NOT NULL,
    [owning_principal_id]                 INT            NULL,
    [sid]                                 VARBINARY (85) NULL,
    [is_fixed_role]                       BIT            NOT NULL,
    [authentication_type]                 INT            NULL,
    [authentication_type_desc]            NVARCHAR (60)  NULL,
    [default_language_name]               NVARCHAR (128) NULL,
    [default_language_lcid]               INT            NULL,
    [allow_encrypted_value_modifications] BIT            NULL,
    CONSTRAINT [PK_DatabasePrincipals] PRIMARY KEY CLUSTERED ([DatabaseID] ASC, [principal_id] ASC),
    CONSTRAINT [FK_DatabasePrincipals_Database] FOREIGN KEY ([DatabaseID]) REFERENCES [dbo].[Databases] ([DatabaseID])
);

