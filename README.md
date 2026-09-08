# Haku — a VR museum with a guide that will not make things up

Haku is a fully virtual museum room for the Meta Quest 3. You walk up to an
object, hold the controller trigger, and ask a question out loud. A local AI
guide answers in a spoken sentence or two — grounded on a curated catalogue
record for that specific object, and required to say *"the record doesn't say"*
when the record does not say.

Everything runs on the team's own laptop over the LAN. **No cloud service, no
API key, no account, no per-request cost.** That is a design constraint, not an
accident: the interesting engineering problem here is a guide that declines.

---

## Before you clone: two rules that protect the whole team

1. **Run `git lfs install` once per machine before cloning.** Skip it and every
   texture and mesh arrives as a ~130-byte pointer file, Unity imports garbage,
   and you will think the repo is broken.
2. **Clone once. Never re-clone.** A fresh clone re-downloads every LFS object.
   Six people × 1.5 GB = 9 GiB, which is the team's entire monthly LFS
   bandwidth in one lab session. When a merge goes wrong, fix it with
   `git reset --hard origin/main` — never by deleting the folder.

Never commit model weights. `.gitignore` explains exactly why.

---

## Repo layout
