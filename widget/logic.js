function refresh(ctx, page) {
  ctx.state.requestedPage = String(page);
  ctx.dispatch({ type: "source.refresh", targetId: "dorm" });
}

export function changeTab(ctx) {
  refresh(ctx, 1);
}

export function changePage(ctx) {
  const current = Number(ctx.state.page);
  const target = Math.max(1, Math.min(Number(ctx.state.pageCount), current + Number(ctx.args.step)));
  if (target !== current) refresh(ctx, target);
}
