-- Each bounded context owns its own database: no shared tables, no cross-context joins,
-- and either service can be moved to its own cluster later without touching the other.
CREATE DATABASE cashflow_launches;
CREATE DATABASE cashflow_consolidation;
