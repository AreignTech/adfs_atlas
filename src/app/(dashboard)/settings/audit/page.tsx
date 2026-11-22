"use client"

import * as React from "react"
import {
  Card,
  CardContent,
  CardDescription,
  CardHeader,
  CardTitle,
} from "@/components/ui/card"
import { Badge } from "@/components/ui/badge"
import { toast } from "@/components/ui/toast"
import { getRbacAudit } from "@/db/actions/rbac"

export default function RbacAuditPage() {
  const [records, setRecords] = React.useState<
    Awaited<ReturnType<typeof getRbacAudit>>
  >([])

  React.useEffect(() => {
    getRbacAudit()
      .then(setRecords)
      .catch((error) => {
        toast.add({
          type: "error",
          title: "Could not load audit logs",
          description: error instanceof Error ? error.message : "Access denied.",
        })
      })
  }, [])

  return (
    <div className="flex flex-col gap-6 px-4 lg:px-6">
      <div>
        <h1 className="text-3xl font-bold">Audit Logs</h1>
        <p className="text-muted-foreground">
          Administrative and authorization security events.
        </p>
      </div>
      <Card>
        <CardHeader>
          <CardTitle>RBAC Activity</CardTitle>
          <CardDescription>Most recent 200 events.</CardDescription>
        </CardHeader>
        <CardContent className="flex flex-col gap-3">
          {records.length === 0 ? (
            <p className="text-sm text-muted-foreground">No RBAC events recorded.</p>
          ) : (
            records.map((record) => (
              <div key={record.id} className="rounded-lg border p-3">
                <div className="flex items-center justify-between gap-3">
                  <span className="font-medium">{record.action}</span>
                  <Badge variant={record.success ? "secondary" : "destructive"}>
                    {record.success ? "Success" : "Failed"}
                  </Badge>
                </div>
                <p className="mt-1 text-xs text-muted-foreground">
                  {record.actorEmail ?? "System"} ·{" "}
                  {record.createdAt.toLocaleString()}
                </p>
                {record.diagnostic && (
                  <p className="mt-2 text-sm">{record.diagnostic}</p>
                )}
              </div>
            ))
          )}
        </CardContent>
      </Card>
    </div>
  )
}
