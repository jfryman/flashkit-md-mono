# Hardware validation checklist (Stage 5)

Manual validation against the real FlashKit MD programmer — never a CI gate.
Run in this order (least- to most-destructive) and record results below.
Compare against dumps produced by the original Windows client where possible.

| # | Test | Linux | macOS | Windows |
|---|------|-------|-------|---------|
| 1 | `info` on a known cart — name/size/RAM match the original client | ☐ | ☐ | ☐ |
| 2 | `read-rom` — MD5 identical to a dump from the original client | ☐ | ☐ | ☐ |
| 3 | `read-ram` on a save cart, then `write-ram` round-trip | ☐ | ☐ | ☐ |
| 4 | `write-rom` to a FlashKit cart — verify passes, cart boots on console | ☐ | ☐ | ☐ |

Notes / discrepancies:

- (record programmer serial port, cart used, OS version, and any diffs here)
