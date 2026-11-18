// Managed by aghouse (opencode-plugin __VERSION__). Written by `aghouse hooks install opencode`, or
// Settings > Integrations > Connect OpenCode; delete this folder to remove it.
//
// Runs in OpenCode's terminal client, so it sits inside the aghouse pane and inherits that pane's
// AGHOUSE_PANE_ID and AGHOUSE_PANE_TOKEN. It reports the sessions this terminal shows to that pane:
// Working while OpenCode runs, "Needs you" when it asks for a permission or answer, and finished
// when it is done. It reports nothing outside aghouse, and nothing for other terminals' sessions.

const AGHOUSE = __AGHOUSE__;

export default {
  id: "aghouse",
  setup(context) {
    if (!process.env.AGHOUSE_PANE_ID || !process.env.AGHOUSE_PANE_TOKEN || process.env.OPENCODE_CLIENT === "acp") return;

    let disposed = false;
    // One call at a time, in order: a "Working" must never land after the "finished" that followed it.
    let queue = Promise.resolve();
    function run(...args) {
      queue = queue.then(async () => {
        if (disposed && args[0] !== "clear-status") return;
        try {
          const child = Bun.spawn({ cmd: [AGHOUSE, ...args], stdin: "ignore", stdout: "ignore", stderr: "ignore", windowsHide: true });
          await child.exited;
        } catch {}
      });
    }
    // A prompt fires both "enqueued" and "started": say Working once per turn, not once per event.
    let reported;
    const working = () => {
      if (reported === "working") return;
      reported = "working";
      run("set-status", "agent", "Working");
    };
    const notify = (body, finished) => {
      reported = "waiting";
      run("notify", "--title", "OpenCode", "--body", body, ...(finished ? ["--finished"] : []));
    };
    const clear = () => {
      reported = undefined;
      run("clear-status", "agent");
    };

    function session(id) {
      try { return context.data.session.get(id); } catch { return undefined; }
    }
    // A subagent's events belong to the session that started it.
    function rootOf(id) {
      let current = id;
      for (let i = 0; i < 16; i++) {
        const parent = session(current)?.parentID;
        if (!parent) break;
        current = parent;
      }
      return current;
    }
    // The sessions this terminal shows: the one on screen and any open tabs. Unknown (null) when the
    // terminal API isn't there, and then every session of this terminal's folder counts.
    function shown() {
      try {
        const ids = new Set();
        const route = context.ui?.router?.current?.();
        if (route?.type === "session" && route.sessionID) ids.add(rootOf(route.sessionID));
        for (const tab of context.ui?.tabs?.list?.() ?? []) if (tab?.sessionID) ids.add(rootOf(tab.sessionID));
        return ids;
      } catch { return null; }
    }
    function mine(sessionID, location) {
      if (!sessionID) return false;
      const ids = shown();
      if (ids && ids.size > 0) return ids.has(rootOf(sessionID));
      const here = context.location?.directory;
      return !here || !location?.directory || location.directory === here;
    }
    const isRoot = (id) => !session(id)?.parentID;

    const seen = new Set();
    function handle(type, event) {
      if (disposed || !event) return;
      const data = event.data ?? event.properties ?? {};
      if (event.id) {
        if (seen.has(event.id)) return;
        seen.add(event.id);
        if (seen.size > 500) seen.delete(seen.values().next().value);
      }
      const sessionID = type === "form.created" ? data.form?.sessionID : data.sessionID;
      if (!mine(sessionID, event.location)) return;
      switch (type) {
        case "session.execution.started":
          working();
          break;
        case "session.inbox.enqueued":
          if (data.item?.type === "user") working();
          break;
        case "permission.replied":
          working(); // answered: it carries on
          break;
        case "session.execution.succeeded":
          if (isRoot(sessionID)) notify("Finished responding", true);
          break;
        case "session.execution.failed":
          if (isRoot(sessionID)) notify("Stopped with an error", true);
          break;
        case "session.execution.interrupted":
          if (isRoot(sessionID)) clear();
          break;
        case "permission.asked": {
          const what = [data.title, data.permission, data.type].find((s) => typeof s === "string" && s.trim());
          notify(what ? `Permission: ${what.trim()}` : "Asks for permission", false);
          break;
        }
        case "form.created":
          if (data.form?.metadata?.kind === "question") notify("Has a question", false);
          break;
      }
    }

    const types = [
      "session.execution.started", "session.execution.succeeded", "session.execution.failed",
      "session.execution.interrupted", "session.inbox.enqueued", "permission.asked", "permission.replied", "form.created",
    ];
    const stops = [];
    if (typeof context.data?.listen === "function") {
      stops.push(context.data.listen(({ details }) => details && handle(details.type, details)));
    } else if (typeof context.data?.on === "function") {
      for (const type of types) stops.push(context.data.on(type, (event) => handle(type, event)));
    }

    return () => {
      disposed = true;
      for (const stop of stops) { try { stop?.(); } catch {} }
      clear(); // OpenCode is closing: it isn't working any more
      return queue;
    };
  },
};
