# 0045. A dashboard as the navigation root, owning the form and history view models

Status: Accepted

## Context

The app launched straight into `GenerateTrackView`, which owned the `NavigationStack`, the path,
the form's view model and the notification-tap routing. History and Settings were two toolbar
icons on the form. A first-time user landed on a screen with six cards and nothing saying what
the app does, and a job still running was only findable by digging through History.

The fix is a landing screen, which means something other than the form becomes the root. That
decision touches more than one view. Three things were tied to the form being the root:

- The `GenerateTrackViewModel` was a `@State` of the form view. A pushed view is rebuilt each
  time it's popped and pushed again, so a model owned by it loses a half-built progression on
  "back".
- The `JobHistoryEntry` detail destination and the "Watch progress" `fullScreenCover` were
  attached to `HistoryView`. A detail pushed from the dashboard's recent list has no
  `HistoryView` beneath it, so "Watch progress" would set `watchSeed` and nothing would present.
- "Use these settings" and notification taps reset the path to pop back to the form. With the
  form no longer the root, "back" has to mean the dashboard.

## Decision

`HomeView` is the root. It owns the `NavigationStack`, the path, a `GenerateTrackViewModel` and a
`HistoryViewModel`.

- `GenerateTrackView` becomes a pushed destination (`GenerateRoute.new`) that takes the model as
  a `@Bindable` instead of owning it. The draft survives popping back to the dashboard.
- `HomeView` registers every destination (`GenerateRoute`, `HistoryRoute`, `JobHistoryEntry`,
  `PlayerRoute`) and attaches the "Watch progress" cover to the stack itself. The dashboard's
  recent list and the full history list therefore share one detail destination and one
  `HistoryViewModel`. `HistoryView` is reduced to the list.
- "Use these settings" seeds the shared model and sets the path to `[GenerateRoute.new]`, so the
  dashboard is underneath. Notification taps still build `[HistoryRoute.list, entry]`. Because the
  watch cover is no longer part of the path, that handler now also calls `stopWatching()`.
- The dashboard shows the five most recent jobs of any disposition, read with a `@Query` limited
  by `fetchLimit`. Settings stays reachable from the form too, since a missing-key error there
  tells the user to go to Settings.

## Alternatives considered

- **`TabView` (Home / New / History).** It keeps the form alive in its own tab for free. Rejected
  because generation is a linear flow rather than a peer of History, and because each tab has its
  own stack: "Use these settings" would have to switch tabs and seed the form, and a notification
  tap would have to switch tabs and set the path on the right tab's stack. That is the same
  wiring as the chosen design, plus tab selection. This was decided by reasoning about the
  routing, not by building both.
- **Leave the model inside `GenerateTrackView`.** Smallest diff, but the draft is lost on every
  back-swipe, which is the first thing someone does after opening the form to look around.
- **Leave `HistoryView` owning its view model, destination and cover, and register
  `JobHistoryEntry` a second time on the dashboard.** Two registrations of one type on one stack
  are ambiguous, and the dashboard-pushed detail would still have no cover for "Watch progress".
  Hoisting them removes both problems.

## Consequences

- Opening the app no longer lands on the form, so the most common action costs one more tap. The
  dashboard's "New Track" button is the first thing on screen to offset that.
- `HomeView` is now the place where navigation wiring accumulates: two view models, four
  destinations, a cover, a sheet and the notification handler. Each new screen or entry point
  adds to it, and it may need splitting if it grows.
- The form draft lives as long as the process. It is not persisted, so killing the app still
  discards it. The seed-from-history path replaces it without confirmation beyond the existing
  "Load these settings?" dialog.
- Anything that opens a job detail must go through the shared `HistoryViewModel`, or "Watch
  progress" won't have a cover. The detail view's previews no longer show navigation out of the
  list, because the destinations aren't registered inside `HistoryView`.
- The dashboard already shows running jobs in the recent list, but only as a static row with the
  in-flight icon. Polling for them and surfacing a status banner is a separate follow-up; it can
  build on the shared `HistoryViewModel` and `JobReconciler` without further restructuring.
