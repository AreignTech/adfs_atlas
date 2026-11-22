CREATE TABLE dbo.api_monitoring_config (
    id INT NOT NULL CONSTRAINT PK_api_monitoring_config PRIMARY KEY CONSTRAINT CK_api_monitoring_config_singleton CHECK (id = 1),
    configuration NVARCHAR(MAX) NOT NULL CONSTRAINT CK_api_monitoring_config_json CHECK (ISJSON(configuration) = 1),
    updated_at DATETIMEOFFSET NOT NULL DEFAULT SYSUTCDATETIME()
);
CREATE TABLE dbo.api_diagnostic_record (
    id UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_api_diagnostic_record PRIMARY KEY,
    kind VARCHAR(16) NOT NULL,
    timestamp DATETIMEOFFSET NOT NULL,
    request_id UNIQUEIDENTIFIER NULL,
    correlation_id UNIQUEIDENTIFIER NULL,
    trace_id VARCHAR(32) NULL,
    user_id NVARCHAR(255) NULL,
    method VARCHAR(10) NULL,
    endpoint NVARCHAR(512) NULL,
    status INT NOT NULL DEFAULT 0,
    duration_ms FLOAT NOT NULL DEFAULT 0,
    is_slow BIT NOT NULL DEFAULT 0,
    component NVARCHAR(255) NULL,
    severity VARCHAR(16) NULL,
    payload NVARCHAR(MAX) NOT NULL CONSTRAINT CK_api_diagnostic_record_json CHECK (ISJSON(payload) = 1)
);
CREATE INDEX IX_api_diagnostic_time ON dbo.api_diagnostic_record(kind, timestamp DESC) INCLUDE(method, endpoint, status, duration_ms, is_slow);
CREATE INDEX IX_api_diagnostic_request ON dbo.api_diagnostic_record(request_id) INCLUDE(timestamp);
CREATE INDEX IX_api_diagnostic_correlation ON dbo.api_diagnostic_record(correlation_id, timestamp DESC);
CREATE INDEX IX_api_diagnostic_user ON dbo.api_diagnostic_record(user_id, timestamp DESC);
CREATE INDEX IX_api_diagnostic_endpoint ON dbo.api_diagnostic_record(endpoint, timestamp DESC) INCLUDE(status, duration_ms);
CREATE INDEX IX_api_diagnostic_status ON dbo.api_diagnostic_record(kind, status, timestamp DESC);
