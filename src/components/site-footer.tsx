export function SiteFooter() {
  return (
    <footer className="border-t bg-background">
      <div className="px-4 py-6 lg:px-6">
        <div className="flex flex-col items-center justify-center gap-2 text-center">
          <p className="text-sm text-muted-foreground">
            ATLAS — ADFS Task Lifecycle and Assignment System
          </p>
          <p className="text-xs text-muted-foreground">
            Projects, tasks, deadlines and progress in one workspace.
          </p>
        </div>
      </div>
    </footer>
  )
}