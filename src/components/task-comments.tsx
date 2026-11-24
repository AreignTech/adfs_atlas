"use client"

import { useCallback, useEffect, useRef, useState } from "react"
import { format, parseISO } from "date-fns"
import { Avatar, AvatarFallback, AvatarImage } from "@/components/ui/avatar"
import { Button } from "@/components/ui/button"
import { Textarea } from "@/components/ui/textarea"
import { useFlowBoard } from "@/data/flowboard-store"
import { addTaskComment, deleteTaskComment, getTaskComments, updateTaskComment, type TaskComment } from "@/lib/api/task-comments"

export function TaskComments({ taskId }: { taskId: string }) {
  const { setTaskCommentCount } = useFlowBoard()
  const [comments, setComments] = useState<TaskComment[]>([])
  const [total, setTotal] = useState(0)
  const [canComment, setCanComment] = useState(false)
  const [loading, setLoading] = useState(true)
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState("")
  const [content, setContent] = useState("")
  const [editing, setEditing] = useState<string | null>(null)
  const [editText, setEditText] = useState("")
  const [deleting, setDeleting] = useState<string | null>(null)
  const active = useRef(true)

  const load = useCallback(async (offset = 0, signal?: AbortSignal) => {
    const page = await getTaskComments(taskId, offset, signal)
    if (!active.current || signal?.aborted) return
    setComments((current) => offset === 0 ? page.items : [...current, ...page.items.filter((item) => !current.some((c) => c.id === item.id))])
    setTotal(page.total)
    setCanComment(page.canComment)
    setTaskCommentCount(taskId, page.total)
  }, [taskId, setTaskCommentCount])

  useEffect(() => {
    active.current = true
    const controller = new AbortController()
    load(0, controller.signal)
      .catch((err) => { if (!controller.signal.aborted) setError(message(err)) })
      .finally(() => { if (!controller.signal.aborted) setLoading(false) })
    return () => { active.current = false; controller.abort() }
  }, [load])

  async function mutate(operation: () => Promise<void>) {
    setBusy(true)
    setError("")
    try { await operation() }
    catch (err) { if (active.current) setError(message(err)) }
    finally { if (active.current) setBusy(false) }
  }

  return (
    <section aria-label="Task comments" className="space-y-4 border-t pt-4">
      <div className="flex items-center justify-between gap-2">
        <h3 className="font-medium">Comments ({total})</h3>
        <span className="text-xs text-muted-foreground">Newest first</span>
      </div>
      {error && <div role="alert" className="space-y-2 text-sm text-destructive">
        <p>{error}</p>
        <Button size="sm" variant="outline" disabled={busy} onClick={() => mutate(async () => { await load() })}>Retry loading comments</Button>
      </div>}
      {loading ? <p role="status" className="text-sm text-muted-foreground">Loading comments…</p> : <>
        {!error && comments.length === 0 && <p className="text-sm text-muted-foreground">No comments yet.</p>}
        <div className="space-y-4">
          {comments.map((comment) => <article key={comment.id} className="flex gap-3 rounded-md border p-3">
            <Avatar className="size-8">
              {comment.authorImage && <AvatarImage src={comment.authorImage} alt={comment.authorName} />}
              <AvatarFallback>{comment.authorName.split(" ").filter(Boolean).slice(0, 2).map((part) => part[0]).join("").toUpperCase()}</AvatarFallback>
            </Avatar>
            <div className="min-w-0 flex-1 space-y-2">
              <div className="flex flex-wrap items-baseline gap-x-2 gap-y-1">
                <span className="text-sm font-medium">{comment.authorName}</span>
                <time dateTime={comment.createdAt} className="text-xs text-muted-foreground">{timestamp(comment.createdAt)}</time>
                {comment.updatedAt && <span title={timestamp(comment.updatedAt)} className="text-xs text-muted-foreground">(edited)</span>}
              </div>
              {editing === comment.id ? <form className="space-y-2" onSubmit={(event) => {
                event.preventDefault()
                if (!editText.trim() || busy) return
                void mutate(async () => {
                  const updated = await updateTaskComment(taskId, comment.id, editText.trim())
                  if (!active.current) return
                  setComments((current) => current.map((c) => c.id === updated.id ? updated : c))
                  setEditing(null)
                })
              }}>
                <Textarea aria-label="Edit comment" value={editText} onChange={(event) => setEditText(event.target.value)} maxLength={5000} required disabled={busy} />
                <div className="flex gap-2">
                  <Button size="sm" type="submit" disabled={busy || !editText.trim()}>Save comment</Button>
                  <Button size="sm" type="button" variant="ghost" disabled={busy} onClick={() => setEditing(null)}>Cancel</Button>
                </div>
              </form> : <p className="whitespace-pre-wrap break-words text-sm">{comment.content}</p>}
              {editing !== comment.id && deleting !== comment.id && <div className="flex gap-2">
                {comment.canEdit && <Button size="sm" variant="ghost" disabled={busy} onClick={() => { setEditing(comment.id); setEditText(comment.content) }}>Edit</Button>}
                {comment.canDelete && <Button size="sm" variant="ghost" disabled={busy} onClick={() => setDeleting(comment.id)}>Delete</Button>}
              </div>}
              {deleting === comment.id && <div className="space-y-2" role="group" aria-label="Confirm comment deletion">
                <p className="text-sm">Permanently delete this comment?</p>
                <div className="flex gap-2">
                  <Button size="sm" variant="destructive" disabled={busy} onClick={() => mutate(async () => {
                    await deleteTaskComment(taskId, comment.id)
                    if (!active.current) return
                    setComments((current) => current.filter((c) => c.id !== comment.id))
                    setTotal((current) => current - 1)
                    setTaskCommentCount(taskId, total - 1)
                    setDeleting(null)
                    await load()
                  })}>Delete comment</Button>
                  <Button size="sm" variant="ghost" disabled={busy} onClick={() => setDeleting(null)}>Cancel</Button>
                </div>
              </div>}
            </div>
          </article>)}
        </div>
        {comments.length < total && <Button variant="outline" size="sm" disabled={busy} onClick={() => mutate(async () => { await load(comments.length) })}>Load older comments</Button>}
        {canComment && <form className="space-y-2" onSubmit={(event) => {
          event.preventDefault()
          if (!content.trim() || busy) return
          void mutate(async () => {
            const created = await addTaskComment(taskId, content.trim())
            if (!active.current) return
            setContent("")
            setComments((current) => [created, ...current])
            setTotal((current) => current + 1)
            setTaskCommentCount(taskId, total + 1)
            await load()
          })
        }}>
          <label htmlFor={`comment-${taskId}`} className="text-sm font-medium">Write a comment</label>
          <Textarea id={`comment-${taskId}`} value={content} onChange={(event) => setContent(event.target.value)} placeholder="Share an update or ask a question…" maxLength={5000} required disabled={busy} />
          <div className="flex items-center justify-between gap-2">
            <span className="text-xs text-muted-foreground">{content.length}/5000</span>
            <Button type="submit" size="sm" disabled={busy || !content.trim()}>{busy ? "Saving…" : "Add Comment"}</Button>
          </div>
        </form>}
      </>}
    </section>
  )
}

function message(error: unknown) { return error instanceof Error ? error.message : "Could not load or save comments. Please try again." }
function timestamp(value: string) { return format(parseISO(value), "MMM d, yyyy 'at' h:mm a") }
