const byId = (id) => document.getElementById(id);
const lihu = "172.25.100.105";
const periods = [
  ["ThreeDays", "近 3 天"], ["SevenDays", "近 7 天"], ["OneMonth", "近 1 个月"],
  ["SixMonths", "近半年"], ["OneYear", "近 1 年"]
];
const form = byId("settings");
const campus = byId("campus");
const building = byId("building");
const floor = byId("floor");
const roomSelect = byId("roomSelect");
let activeLoad;
let lastConfig;

async function request(path, { query, body, signal } = {}) {
  const url = new URL(path, location.origin);
  if (query) url.search = new URLSearchParams(query);
  const response = await fetch(url, {
    signal,
    ...(body !== undefined && {
      method: "POST",
      headers: { "Content-Type": "application/json", "X-Dorm-Settings": "1" },
      body: JSON.stringify(body)
    })
  });
  const data = await response.json();
  if (!response.ok) throw new Error(data.error || "本机服务暂时无法完成请求。");
  return data;
}

function fill(select, rows, selected = "") {
  select.replaceChildren(new Option("请选择", ""));
  for (const { id, name } of rows) select.add(new Option(name, id));
  select.value = selected;
  if (select.selectedIndex < 0) select.selectedIndex = 0;
}

function showRoomMode() {
  const isLihu = campus.value === lihu;
  byId("floorField").hidden = !isLihu;
  byId("roomSelectField").hidden = !isLihu;
  byId("roomField").hidden = isLihu;
  byId("room").required = !isLihu;
  floor.required = isLihu;
  roomSelect.required = isLihu;
}

async function loadOptions(changed, saved = {}) {
  activeLoad?.abort();
  const load = new AbortController();
  activeLoad = load;
  const { signal } = load;
  const locationStatus = byId("locationStatus");
  [campus, building, floor, roomSelect].forEach((control) => control.disabled = true);
  byId("save").disabled = true;
  locationStatus.textContent = "正在加载选项…";
  try {
    if (changed === "campus") {
      fill(building, [], saved.buildingId);
      fill(floor, []);
      fill(roomSelect, []);
      showRoomMode();
      const buildings = await request("/api/buildings", { query: { campus: campus.value }, signal });
      fill(building, buildings, saved.buildingId);
    }
    if (campus.value === lihu && changed !== "floor") {
      fill(floor, []);
      fill(roomSelect, []);
      if (building.value) {
        const floors = await request("/api/floors", { query: { building: building.value }, signal });
        fill(floor, floors, saved.floorId);
      }
    }
    if (campus.value === lihu) {
      fill(roomSelect, []);
      if (floor.value) {
        const rooms = await request("/api/rooms", {
          query: { building: building.value, floor: floor.value }, signal
        });
        fill(roomSelect, rooms, saved.roomId);
      }
    }
    locationStatus.textContent = "";
  } catch (error) {
    if (signal.aborted) return;
    locationStatus.textContent = error.message;
    if (saved.buildingId) {
      fill(building, [{ id: saved.buildingId, name: saved.buildingName }], saved.buildingId);
      if (campus.value === lihu) {
        fill(floor, [{ id: saved.floorId, name: saved.floorId }], saved.floorId);
        fill(roomSelect, [{ id: saved.roomId, name: saved.roomName }], saved.roomId);
      }
    }
  } finally {
    if (activeLoad === load) {
      [campus, building, floor, roomSelect].forEach((control) => control.disabled = false);
      byId("save").disabled = false;
    }
  }
}

function fillConfig(data) {
  lastConfig = data;
  const settings = data.settings;
  fill(campus, data.campuses.map((c) => ({ id: c.client, name: c.name })), settings.location.client);
  byId("room").value = settings.location.roomName;
  for (const name of ["usagePeriod", "paymentPeriod"]) {
    fill(byId(name), periods.map(([id, label]) => ({ id, name: label })), settings[name]);
  }
  byId("daily").checked = settings.daily;
  byId("low").checked = settings.low;
  byId("dailyTime").value = settings.dailyTime.slice(0, 5);
  byId("lowThreshold").value = settings.lowThreshold;
  byId("keyStatus").textContent = data.keySaved ? "已保存，留空可沿用。" : "尚未保存 SendKey。";
  byId("test").disabled = !data.keySaved;
}

for (const name of ["campus", "building", "floor"]) {
  byId(name).addEventListener("change", () => {
    if (name === "campus") byId("room").value = "";
    loadOptions(name);
  });
}

form.addEventListener("submit", async (event) => {
  event.preventDefault();
  const status = byId("status");
  byId("save").disabled = true;
  status.textContent = "正在验证并保存…";
  const isLihu = campus.value === lihu;
  const roomName = isLihu ? roomSelect.selectedOptions[0]?.text : byId("room").value.trim();
  try {
    const result = await request("/api/config", { body: {
      settings: {
        location: { client: campus.value, buildingId: building.value,
          buildingName: building.selectedOptions[0]?.text || "",
          floorId: isLihu ? floor.value : "", roomId: isLihu ? roomSelect.value : "", roomName },
        usagePeriod: byId("usagePeriod").value, paymentPeriod: byId("paymentPeriod").value,
        daily: byId("daily").checked, low: byId("low").checked,
        dailyTime: byId("dailyTime").value + ":00",
        lowThreshold: Number(byId("lowThreshold").value)
      },
      sendKey: byId("sendKey").value,
      clearKey: byId("clearKey").checked
    } });
    const fresh = await request("/api/config");
    byId("sendKey").value = "";
    byId("clearKey").checked = false;
    byId("test").disabled = !fresh.keySaved;
    byId("keyStatus").textContent = fresh.keySaved ? "已保存，留空可沿用。" : "尚未保存 SendKey。";
    status.textContent = result.message;
  } catch (error) {
    status.textContent = error.message;
  } finally {
    byId("save").disabled = false;
  }
});
byId("test").addEventListener("click", async () => {
  byId("test").disabled = true;
  const status = byId("status");
  status.textContent = "正在发送测试消息…";
  try { status.textContent = (await request("/api/test-push", { body: {} })).message; }
  catch (error) { status.textContent = error.message; }
  finally { byId("test").disabled = false; }
});

try {
  fillConfig(await request("/api/config"));
  await loadOptions("campus", lastConfig.settings.location);
} catch (error) {
  byId("status").textContent = error.message;
}
