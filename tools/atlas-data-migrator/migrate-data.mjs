// Source-only PostgreSQL reader. SQL Server access remains entirely in Dapper.
import pg from "pg";
import { spawn } from "node:child_process";

// Preserve PostgreSQL microseconds and calendar dates; JavaScript Date loses
// sub-millisecond precision and can reinterpret calendar dates in local time.
pg.types.setTypeParser(1184, value => value);
pg.types.setTypeParser(1082, value => value);

const tables = ["lab_code", "section_code", "user", "member", "account", "verification", "session", "project", "task", "project_member", "user_org_assignment", "app_role", "app_permission", "app_role_permission", "app_user_role", "ldap_config", "ldap_identity", "ldap_group_mapping", "org_audit", "rbac_audit", "ldap_audit"];
const source = new pg.Client({ connectionString: process.env.DATABASE_URL, connectionTimeoutMillis: 10000 });
try {
  if (!process.env.DATABASE_URL || !process.env.ConnectionStrings__Atlas) throw new Error("Configure source and target connections.");
  await source.connect();
  await source.query("BEGIN TRANSACTION ISOLATION LEVEL REPEATABLE READ READ ONLY");
  const snapshot = {};
  for (const table of tables) snapshot[table] = (await source.query(`SELECT * FROM public."${table}"`)).rows;
  await source.query("COMMIT");
  const command = process.argv.includes("--repair-timestamp-precision") ? "repair-timestamp-precision" : process.argv.includes("--verify") ? "verify-data" : "import-data";
  const target = spawn("dotnet", ["run", "--no-build", "--project", "backend/Atlas.DbTool", "--", command], { stdio: ["pipe", "inherit", "inherit"], windowsHide: true });
  const completion = new Promise((resolve, reject) => { target.once("error", reject); target.once("exit", code => resolve(code ?? 1)); });
  target.stdin.on("error", () => {});
  target.stdin.end(JSON.stringify(snapshot));
  process.exitCode = await completion;
} catch {
  // Connection strings and source values must never reach console diagnostics.
  console.error("Source snapshot/export failed. Check source availability and configuration; no source writes were performed.");
  process.exitCode = 1;
} finally {
  await source.end();
}
