"use client"

import * as React from "react"
import { Badge } from "@/components/ui/badge"
import { Button } from "@/components/ui/button"
import {
  Card,
  CardContent,
  CardDescription,
  CardHeader,
  CardTitle,
} from "@/components/ui/card"
import { Input } from "@/components/ui/input"
import { Label } from "@/components/ui/label"
import { Select, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select"
import { Textarea } from "@/components/ui/textarea"
import { toast } from "@/components/ui/toast"
import {
  getLdapAdminConfig,
  getLdapGroupMappings,
  saveLdapAdminConfig,
  saveLdapGroupMappings,
  testLdapAdminConnection,
} from "@/lib/api/ldap"

type Config = Awaited<ReturnType<typeof getLdapAdminConfig>>
type Mapping = Awaited<ReturnType<typeof getLdapGroupMappings>>[number]

export default function AuthenticationSettings() {
  const [config, setConfig] = React.useState<Config | null>(null)
  const [mappings, setMappings] = React.useState<Mapping[]>([])
  const [testUsername, setTestUsername] = React.useState("")
  const [saving, setSaving] = React.useState(false)
  const [testing, setTesting] = React.useState(false)
  const [error, setError] = React.useState<string | null>(null)

  React.useEffect(() => {
    Promise.all([getLdapAdminConfig(), getLdapGroupMappings()])
      .then(([ldapConfig, ldapMappings]) => {
        setConfig(ldapConfig)
        setMappings(ldapMappings)
      })
      .catch((loadError) => {
        setError(
          loadError instanceof Error
            ? loadError.message
            : "Could not load LDAP settings."
        )
      })
  }, [])

  async function save() {
    if (!config) return
    setSaving(true)
    setError(null)
    try {
      await saveLdapAdminConfig({
        enabled: config.enabled,
        host: config.host,
        port: config.port,
        protocol: config.protocol as "ldap" | "ldaps" | "starttls",
        baseDn: config.baseDn,
        bindDn: config.bindDn,
        bindPassword: config.bindPassword,
        searchBase: config.searchBase,
        userSearchAttribute: config.userSearchAttribute,
        loginIdentifierAttribute: config.loginIdentifierAttribute,
        emailAttribute: config.emailAttribute,
        displayNameAttribute: config.displayNameAttribute,
        directoryIdentifierAttribute: config.directoryIdentifierAttribute,
        requireSecure: config.requireSecure,
        validateTls: config.validateTls,
        tlsCaCertificate:
          config.tlsCaCertificate === "<configured>"
            ? ""
            : config.tlsCaCertificate,
        connectionTimeout: config.connectionTimeout,
        autoProvision: config.autoProvision,
        requireApproval: config.requireApproval,
      })
      const refreshed = await getLdapAdminConfig()
      setConfig(refreshed)
      toast.add({
        type: "success",
        title: "LDAP configuration saved",
        description: "The settings are stored securely.",
      })
    } catch (saveError) {
      const message =
        saveError instanceof Error
          ? saveError.message
          : "Could not save LDAP configuration."
      setError(message)
      toast.add({ type: "error", title: "Could not save LDAP", description: message })
    } finally {
      setSaving(false)
    }
  }

  async function testConnection() {
    setTesting(true)
    setError(null)
    try {
      const result = await testLdapAdminConnection(testUsername.trim() || undefined)
      toast.add({
        type: "success",
        title: "LDAP test succeeded",
        description: result.message,
      })
      const refreshed = await getLdapAdminConfig()
      setConfig(refreshed)
    } catch (testError) {
      const message =
        testError instanceof Error
          ? testError.message
          : "LDAP connection test failed."
      setError(message)
      toast.add({ type: "error", title: "LDAP test failed", description: message })
    } finally {
      setTesting(false)
    }
  }

  async function saveMappings() {
    try {
      await saveLdapGroupMappings(
        mappings.map((mapping) => ({
          ldapGroup: mapping.ldapGroup,
          atlasRole: mapping.atlasRole,
          enabled: mapping.enabled,
        }))
      )
      toast.add({
        type: "success",
        title: "Group mappings saved",
        description: "LDAP role mappings were updated.",
      })
    } catch (mappingError) {
      toast.add({
        type: "error",
        title: "Could not save mappings",
        description:
          mappingError instanceof Error
            ? mappingError.message
            : "Please try again.",
      })
    }
  }

  function update<K extends keyof Config>(key: K, value: Config[K]) {
    setConfig((current) => (current ? { ...current, [key]: value } : current))
  }

  if (error && !config) {
    return (
      <div className="px-4 lg:px-6">
        <Card>
          <CardHeader>
            <CardTitle>Authentication Settings</CardTitle>
            <CardDescription>{error}</CardDescription>
          </CardHeader>
        </Card>
      </div>
    )
  }

  if (!config) {
    return <p className="px-4 text-sm text-muted-foreground lg:px-6">Loading LDAP settings...</p>
  }

  return (
    <div className="flex flex-col gap-6 px-4 lg:px-6">
      <div className="flex items-center justify-between gap-4">
        <div>
          <h1 className="text-3xl font-bold">Authentication Settings</h1>
          <p className="text-muted-foreground">
            Manage LDAP authentication and directory role mappings.
          </p>
        </div>
        <Badge variant={config.enabled ? "default" : "secondary"}>
          LDAP {config.enabled ? "Enabled" : "Disabled"}
        </Badge>
      </div>

      {error && (
        <p role="alert" className="text-sm text-destructive">{error}</p>
      )}

      <Card>
        <CardHeader>
          <CardTitle>LDAP Configuration</CardTitle>
          <CardDescription>
            Configure LDAPS or StartTLS for enterprise directory authentication.
          </CardDescription>
        </CardHeader>
        <CardContent className="grid gap-5 md:grid-cols-2">
          <Field label="Enable LDAP Authentication">
            <label className="flex items-center gap-2 text-sm">
              <input
                type="checkbox"
                checked={config.enabled}
                onChange={(event) => update("enabled", event.target.checked)}
              />
              Enable LDAP authentication
            </label>
          </Field>
          <Field label="Protocol">
            <Select
              value={config.protocol}
              onValueChange={(value) =>
                update("protocol", value as "ldap" | "ldaps" | "starttls")
              }
            >
              <SelectTrigger><SelectValue /></SelectTrigger>
              <SelectItem value="ldaps">LDAPS</SelectItem>
              <SelectItem value="starttls">StartTLS</SelectItem>
              <SelectItem value="ldap">LDAP (insecure)</SelectItem>
            </Select>
          </Field>
          <Field label="LDAP Server / Hostname">
            <Input value={config.host} onChange={(event) => update("host", event.target.value)} />
          </Field>
          <Field label="LDAP Port">
            <Input type="number" value={config.port} onChange={(event) => update("port", Number(event.target.value))} />
          </Field>
          <Field label="Base DN">
            <Input value={config.baseDn} onChange={(event) => update("baseDn", event.target.value)} />
          </Field>
          <Field label="Search Base">
            <Input value={config.searchBase} onChange={(event) => update("searchBase", event.target.value)} />
          </Field>
          <Field label="Bind DN">
            <Input value={config.bindDn} onChange={(event) => update("bindDn", event.target.value)} />
          </Field>
          <Field label={config.bindPasswordSet ? "Bind Password (masked; enter to replace)" : "Bind Password"}>
            <Input type="password" value={config.bindPassword} onChange={(event) => update("bindPassword", event.target.value)} />
          </Field>
          <Field label="User Search Attribute">
            <Input value={config.userSearchAttribute} onChange={(event) => update("userSearchAttribute", event.target.value)} />
          </Field>
          <Field label="Login Identifier Attribute">
            <Input value={config.loginIdentifierAttribute} onChange={(event) => update("loginIdentifierAttribute", event.target.value)} />
          </Field>
          <Field label="Email Attribute">
            <Input value={config.emailAttribute} onChange={(event) => update("emailAttribute", event.target.value)} />
          </Field>
          <Field label="Display Name Attribute">
            <Input value={config.displayNameAttribute} onChange={(event) => update("displayNameAttribute", event.target.value)} />
          </Field>
          <Field label="Directory Identifier Attribute">
            <Input value={config.directoryIdentifierAttribute} onChange={(event) => update("directoryIdentifierAttribute", event.target.value)} />
          </Field>
          <Field label="Connection Timeout (ms)">
            <Input type="number" value={config.connectionTimeout} onChange={(event) => update("connectionTimeout", Number(event.target.value))} />
          </Field>
          <Field label="TLS CA Certificate">
            <Textarea value={config.tlsCaCertificate} onChange={(event) => update("tlsCaCertificate", event.target.value)} placeholder="PEM certificate or leave blank to use system trust" />
          </Field>
          <div className="flex flex-col gap-3 md:col-span-2">
            <label className="flex items-center gap-2 text-sm">
              <input type="checkbox" checked={config.requireSecure} onChange={(event) => update("requireSecure", event.target.checked)} />
              Require secure LDAP connections
            </label>
            <label className="flex items-center gap-2 text-sm">
              <input type="checkbox" checked={config.validateTls} onChange={(event) => update("validateTls", event.target.checked)} />
              Validate TLS certificates
            </label>
            <label className="flex items-center gap-2 text-sm">
              <input type="checkbox" checked={config.autoProvision} onChange={(event) => update("autoProvision", event.target.checked)} />
              Automatically provision directory users
            </label>
            <label className="flex items-center gap-2 text-sm">
              <input type="checkbox" checked={config.requireApproval} onChange={(event) => update("requireApproval", event.target.checked)} />
              Require administrator approval before first LDAP login
            </label>
          </div>
        </CardContent>
      </Card>

      <Card>
        <CardHeader>
          <CardTitle>Connection Test</CardTitle>
          <CardDescription>
            Status: {config.status} &nbsp;•&nbsp;
            Last successful test: {config.lastSuccessfulTestAt ?? "Never"}
          </CardDescription>
        </CardHeader>
        <CardContent className="flex flex-col gap-3 sm:flex-row">
          <Input
            className="sm:max-w-sm"
            value={testUsername}
            onChange={(event) => setTestUsername(event.target.value)}
            placeholder="Optional test username"
          />
          <Button type="button" variant="outline" onClick={testConnection} disabled={testing}>
            {testing ? "Testing..." : "Test LDAP Connection"}
          </Button>
        </CardContent>
      </Card>

      <Card>
        <CardHeader>
          <CardTitle>LDAP Group-to-Role Mapping</CardTitle>
          <CardDescription>
            Directory group membership can assign an ATLAS application role.
          </CardDescription>
        </CardHeader>
        <CardContent className="flex flex-col gap-3">
          {mappings.map((mapping, index) => (
            <div key={mapping.id ?? index} className="grid gap-2 md:grid-cols-[1fr_220px_100px]">
              <Input
                value={mapping.ldapGroup}
                onChange={(event) =>
                  setMappings((current) =>
                    current.map((item, itemIndex) =>
                      itemIndex === index ? { ...item, ldapGroup: event.target.value } : item
                    )
                  )
                }
              />
              <Select
                value={mapping.atlasRole}
                onValueChange={(value) =>
                  setMappings((current) =>
                    current.map((item, itemIndex) =>
                      itemIndex === index
                        ? { ...item, atlasRole: value ?? "Member" }
                        : item
                    )
                  )
                }
              >
                <SelectTrigger><SelectValue /></SelectTrigger>
                <SelectItem value="Administrator">Administrator</SelectItem>
                <SelectItem value="Project Manager">Project Manager</SelectItem>
                <SelectItem value="Member">Member</SelectItem>
              </Select>
              <Button
                type="button"
                variant="outline"
                onClick={() =>
                  setMappings((current) => current.filter((_, itemIndex) => itemIndex !== index))
                }
              >
                Remove
              </Button>
            </div>
          ))}
          <div className="flex flex-wrap gap-2">
            <Button
              type="button"
              variant="outline"
              onClick={() =>
                setMappings((current) => [
                  ...current,
                  {
                    id: crypto.randomUUID(),
                    ldapGroup: "",
                    atlasRole: "Member",
                    enabled: true,
                    updatedAt: new Date().toISOString(),
                  } as Mapping,
                ])
              }
            >
              Add Mapping
            </Button>
            <Button type="button" variant="outline" onClick={saveMappings}>
              Save Mappings
            </Button>
          </div>
        </CardContent>
      </Card>

      <div className="flex justify-end">
        <Button type="button" onClick={save} disabled={saving}>
          {saving ? "Saving..." : "Save LDAP Configuration"}
        </Button>
      </div>
    </div>
  )
}

function Field({
  label,
  children,
}: {
  label: string
  children: React.ReactNode
}) {
  return (
    <div className="flex flex-col gap-2">
      <Label>{label}</Label>
      {children}
    </div>
  )
}
