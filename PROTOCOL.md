# ONX-100 Protocol (Reverse-Engineered)

This is the actual protocol of the ONX-100 presentation switcher, discovered
by black-box testing against the device simulator (firmware 2.13,
`127.0.0.1:4999`).

The vendor document (`ONX-100_Protocol_Excerpt.pdf`) is incomplete and
sometimes wrong. Where the device disagrees with the vendor excerpt, the
device wins. Differences are called out inline and summarized at the end.

---

## Transport

TCP, port 4999. ASCII text. One command per line.

- **Host to device:** command text followed by `CR` (`0x0D`).
- **Device to host:** response text followed by `CR LF` (`0x0D 0x0A`).
- `LF` alone does **not** terminate a command.
- Commands are **case-sensitive** (`pwr ?` is not `PWR ?`).
- Whitespace is **significant** -- leading spaces, trailing spaces, extra
  spaces between tokens, and tabs all change meaning. Do not trim or
  normalize.
- Multiple commands can be sent on a single connection (no reconnect needed).

---

## Connection

### Greeting

On connect, the device immediately sends a greeting before accepting commands:

```
*HELLO ONX-100 FW:2.13\r\n
```

> **Not in vendor docs.** A driver must read and discard this line before
> sending anything.

### Single-Client Restriction

Only one TCP client at a time. A second client receives:

```
*BUSY\r\n
```

and the connection is closed. A new connection succeeds once the first client
disconnects.

> **Not in vendor docs.**

### Idle Timeout

An idle connection is closed after approximately 60 seconds. Before closing,
the device sends:

```
BYE\r\n
```

> **Not in vendor docs.**

---

## Power

Power has **four states**, not two. This is the most important difference from
the vendor excerpt.

```
        PWR ON / OK              ~8 sec              PWR OFF / OK             ~5 sec
 [OFF] ──────────────> [WARM] ──────────────> [ON] ──────────────> [COOL] ──────────────> [OFF]
                                EVT PWR ON                                 EVT PWR OFF
```

| Command | Response | Notes |
|---------|----------|-------|
| `PWR ON` | `OK` | Begins warm-up (OFF -> WARM -> ON). No-op if already ON. |
| `PWR OFF` | `OK` | Begins cool-down (ON -> COOL -> OFF). No-op if already OFF. |
| `PWR ?` | `PWR OFF` / `PWR WARM` / `PWR ON` / `PWR COOL` | Works in any state. |

### Transition behavior

- `PWR ON` when already ON: returns `OK`, nothing happens.
- `PWR OFF` when already OFF: returns `OK`, nothing happens.
- Any power command during WARM or COOL: returns `ERR 03`.
- When a transition completes, the device sends an unsolicited event:
  - `EVT PWR ON\r\n` at the end of warm-up.
  - `EVT PWR OFF\r\n` at the end of cool-down.
- **Do not hard-code timing.** The `EVT PWR` event is the
  only reliable way to know a transition has finished.

### Measured transition times

| Transition | Duration | Samples |
|------------|----------|---------|
| Warm-up (OFF -> ON) | ~8.0 s | 3 |
| Cool-down (ON -> OFF) | ~5.0 s | 3 |

These are approximate. Not enough samples to establish variance.

---

## Input Selection

| Command | Response | Notes |
|---------|----------|-------|
| `IN 1` .. `IN 4` | `OK` | Selects the input. |
| `IN ?` | `IN 1` .. `IN 4` | Returns the currently selected input. |

### State restriction

Input commands **only work when power is fully ON**. In OFF, WARM, and COOL
they return `ERR 03` -- even for invalid input numbers like `IN 5`.

> **Not in vendor docs.**

When power is ON:
- `IN 0` and `IN 5` return `ERR 02` (out of range).
- Valid range is 1--4.

The error priority here is notable: the device checks power-state eligibility
*before* validating the argument. `IN 99` in the OFF state returns `ERR 03`,
not `ERR 02`.

---

## Volume

**This works differently from the vendor excerpt.** The vendor excerpt implies
both setter and query use the same numeric format. They do not.

| Command | Response | Notes |
|---------|----------|-------|
| `VOL <0-100>` | `OK` | Argument is **decimal**, range 0--100. |
| `VOL ?` | `VOL <hex>` | Response is **two-digit uppercase hex**. |

### Setter rules

The volume setter accepts a plain decimal integer with no leading zeros:

| Accepted | Rejected (ERR 02) |
|----------|-------------------|
| `VOL 0` | `VOL -1` (negative) |
| `VOL 10` | `VOL 01` (leading zero) |
| `VOL 100` | `VOL 101` (out of range) |
| | `VOL 255`, `VOL 256` (out of range) |
| | `VOL FF`, `VOL 0A` (hex not accepted) |
| | `VOL 0x10` (prefix notation) |
| | `VOL abc` (non-numeric) |

### Query response encoding

The query returns the volume as a two-digit uppercase hex string:

| After setting | `VOL ?` returns | Conversion |
|---------------|-----------------|------------|
| `VOL 0` | `VOL 00` | 0x00 = 0 |
| `VOL 10` | `VOL 0A` | 0x0A = 10 |
| `VOL 64` | `VOL 40` | 0x40 = 64 |
| `VOL 100` | `VOL 64` | 0x64 = 100 |

The response is the device's internal value, not an echo of the command. A
driver must parse the hex and expose a 0--100 integer to consumers.

### State availability

`VOL ?` works in all power states (tested in OFF, WARM, ON, COOL).
The setter (`VOL <n>`) was only tested while ON. Its behavior in other states
is unknown.

---

## Mute

| Command | Response | Notes |
|---------|----------|-------|
| `MUTE ON` | `OK` | Mute audio. |
| `MUTE OFF` | `OK` | Unmute audio. |
| `MUTE ?` | `MUTE ON` / `MUTE OFF` | Query mute state. |

Mute commands work in **all power states** (tested in OFF, WARM, ON, COOL).

Whether mute state persists across power cycles is unknown.

---

## Asynchronous Events

The device sends unsolicited lines prefixed with `EVT`. These are **not**
responses to commands. A driver must never treat an `EVT` line as a command
response.

### Event types

| Event line | Meaning |
|------------|---------|
| `EVT PWR ON` | Warm-up finished; device is now ON. |
| `EVT PWR OFF` | Cool-down finished; device is now OFF. |
| `EVT SIGNAL <n> OK` | Signal detected on source `<n>`. |
| `EVT SIGNAL <n> LOST` | Signal lost on source `<n>`. |

> **Not in vendor docs.** None of these events are mentioned in the excerpt.

### Signal events

Key observations:
- Signal events arrive spontaneously, without any preceding command.
- The number `<n>` is probably the physical input, but this is unproven.
  Signal 4 events were seen while input 1 was selected, so the number is
  definitely not "the current input."
- Signal loss does **not** trigger automatic input switching or power changes.
  With input 2 selected, signal-loss events for inputs 1, 3, and 4 arrived;
  `IN ?` still returned `IN 2` and `PWR ?` still returned `PWR ON`.

### Interleaving with responses

Events can arrive in the same TCP read as a command response:

```
-> PWR ?\r
<- PWR ON\r\n                       # response to PWR ?
<- EVT SIGNAL 4 LOST\r\n            # unrelated event

-> IN ?\r
<- EVT SIGNAL 2 OK\r\n              # event arrives first
<- IN 2\r\n                         # actual response to IN ?
```

A driver must parse each line independently. Lines starting with `EVT ` go to
the event handler; everything else is matched to the pending command.

---

## Errors

| Code | Meaning |
|------|---------|
| `ERR 01` | Unknown command (keyword not recognized). |
| `ERR 02` | Invalid parameter (known command, bad argument). |
| `ERR 03` | Command unavailable in current power state. |

> `ERR 03` is **not in the vendor docs**. It is the device's way of saying
> "I know that command, but I can't do it right now."

### Error priority

The device checks in this order (confirmed for `IN` commands):

1. Is the keyword recognized? If not -> `ERR 01`.
2. Is the command category available in the current power state? If not -> `ERR 03`.
3. Is the argument valid? If not -> `ERR 02`.

This means `IN 5` returns `ERR 03` when the device is OFF (state check first),
but `ERR 02` when the device is ON (argument check).

### Tested error cases

| Sent | Response | Reason |
|------|----------|--------|
| `Something` | `ERR 01` | Unknown keyword |
| `pwr ?` | `ERR 01` | Wrong case |
| ` IN ?` | `ERR 01` | Leading space |
| *(empty)* | `ERR 01` | No keyword |
| `PWR  ON` | `ERR 02` | Double space |
| `PWR MAYBE` | `ERR 02` | Invalid PWR argument |
| `IN` | `ERR 02` | Missing argument |
| `VOL` | `ERR 02` | Missing argument |
| `MUTE` | `ERR 02` | Missing argument |
| `VOL abc` | `ERR 02` | Non-numeric |
| `VOL 01` | `ERR 02` | Leading zero |
| `VOL 0A` | `ERR 02` | Hex in setter |
| `VOL -1` | `ERR 02` | Negative |
| `VOL 101` | `ERR 02` | Over max |
| `IN 0` *(when ON)* | `ERR 02` | Below range |
| `IN 5` *(when ON)* | `ERR 02` | Above range |
| `IN 1` *(when OFF)* | `ERR 03` | Input unavailable |
| `PWR OFF` *(during WARM)* | `ERR 03` | Transition in progress |
| `PWR ON` *(during COOL)* | `ERR 03` | Transition in progress |

---

## Command Availability by Power State

| Command | OFF | WARM | ON | COOL |
|---------|-----|------|----|------|
| `PWR ON` | OK (starts warm-up) | ERR 03 | OK (no-op) | ERR 03 |
| `PWR OFF` | OK (no-op) | ERR 03 | OK (starts cool-down) | ERR 03 |
| `PWR ?` | OK | OK | OK | OK |
| `IN <n>` | ERR 03 | ERR 03 | OK | ERR 03 |
| `IN ?` | ERR 03 | ERR 03 | OK | ERR 03 |
| `VOL <n>` (set) | ? | ? | OK | ? |
| `VOL ?` | OK | OK | OK | OK |
| `MUTE ON/OFF` | OK | OK | OK | OK |
| `MUTE ?` | OK | OK | OK | OK |

---

## Example Session

```
# Connect
<- *HELLO ONX-100 FW:2.13\r\n

# Power on
-> PWR ?\r
<- PWR OFF\r\n

-> PWR ON\r
<- OK\r\n

-> PWR ?\r
<- PWR WARM\r\n

    ... ~8 seconds ...

<- EVT PWR ON\r\n                   # transition complete

# Select input, set volume
-> IN 2\r
<- OK\r\n

-> VOL 75\r
<- OK\r\n

-> VOL ?\r
<- VOL 4B\r\n                       # 0x4B = 75

# Mute
-> MUTE ON\r
<- OK\r\n

# Power off
-> PWR OFF\r
<- OK\r\n

    ... ~5 seconds ...

<- EVT PWR OFF\r\n
```

---

## Differences from Vendor Excerpt

The vendor excerpt (`ONX-100_Protocol_Excerpt.pdf`, rev 1.02) describes a
simpler protocol. The real device has these additional behaviors:

| # | What the vendor excerpt says (or omits) | What the device actually does |
|---|----------------------------------------|-------------------------------|
| 1 | No mention of a greeting | Sends `*HELLO ONX-100 FW:2.13` on connect |
| 2 | No connection limit mentioned | Only one client; second gets `*BUSY` |
| 3 | `PWR ?` returns `PWR ON` / `PWR OFF` | Also returns `PWR WARM` and `PWR COOL` |
| 4 | Power changes appear instantaneous | Warm-up takes ~8 s, cool-down takes ~5 s |
| 5 | No events mentioned | `EVT PWR ON/OFF` and `EVT SIGNAL <n> OK/LOST` |
| 6 | Only `ERR 01` and `ERR 02` | `ERR 03` exists (command unavailable in current state) |
| 7 | Input commands always available | Only work when power is fully ON |
| 8 | No mention of case/whitespace sensitivity | Strictly case-sensitive; whitespace-significant |
| 9 | `VOL ?` returns decimal | `VOL ?` returns two-digit uppercase hex |
| 10 | No idle timeout mentioned | ~60 s idle timeout; device sends `BYE` then disconnects |

---
