import { notFound } from "next/navigation"
import { ManagementCenter } from "../components/management-center"

export default async function ApiManagementPage({ params }: { params: Promise<{ section: string }> }) {
  const { section } = await params
  if (!["documentation", "activity", "diagnostics", "configuration"].includes(section)) notFound()
  return <ManagementCenter section={section} />
}
