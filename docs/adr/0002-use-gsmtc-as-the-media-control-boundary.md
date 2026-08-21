# Use GSMTC as the media control boundary

Media Lock will discover, observe and control playback through Windows GSMTC rather than browser automation or
simulated media keystrokes. GSMTC provides a stable system-level abstraction across cooperating applications but
does not guarantee browser URLs or durable Session identity, so Session Fingerprint and Recovery are first-class
domain concerns and optional browser correlation remains outside the MVP.
