# Flow Music in the owner's Chrome (finals)

Google Flow Music (flowmusic.app, Lyria) through **Claude in Chrome**, in the owner's own signed-in Chrome. It spends the
Flow Music credits that come with Google AI Plus (Starter: 3,000 a month, about 5 a song). There is no middleman and no
extra cost. The page can change without warning: when a step below no longer matches the page, stop and tell the owner
what is different. Never guess at buttons that spend credits.

1. Load the Chrome tools in one ToolSearch call, list the tabs, and open flowmusic.app in a **new tab**. Never type a
   password. If the page asks the owner to sign in, they do it themselves.
2. Flow is a chat with a "Producer" (20 Oct 2026): the home page's box ("Let's create...") takes the prompt and the send
   arrow starts a session. There is **no instrumental switch**: start the prompt with "Instrumental only, no vocals, no
   lyrics, no voices of any kind." (the owner's rule), then the cue's BPM, key, length and sound. The settings button
   beside the box sets the model (Instrument: Lyria 3.5), Ghostwriter (lyrics) and Producer; leave them. A song takes
   under a minute and lands as a card in the chat.
3. Extend, trim, fade or split stems (drums, bass, other) with Flow's own tools as the cue needs. Stems are for the
   adaptive layers in the Sound Bible.
4. The card's `...` menu has Download (Download video, Get stems, M4A, MP3, **WAV**), Split stems, Remix and Details.
   WAV lands in the owner's Downloads as `<Song title>.wav` (about 33 MB for 2:50); move it into
   `G:\My Drive\KryticalNova\Audio\Music\KN-Originals\<batch>\` as `<Cue>_flow<n>.wav`, with stems as
   `<Cue>_flow<n>_<stem>.wav`.
5. Add a `cues.json` line for each file with `scripts/add_take.py`: `tool: "Google Flow Music"`, `model` (as Flow names it), the prompt, the
   credits spent, the plan (AI Plus / Flow Music Starter, which includes commercial use rights), and the date.
6. Run `check.py` and `audition.py` again, then republish the same audition artifact (the Flow takes join their slot).

Commercial use: the AI Plus Flow Music Starter plan lists commercial rights. Keep the `cues.json` lines as the record
of which plan made each file.
