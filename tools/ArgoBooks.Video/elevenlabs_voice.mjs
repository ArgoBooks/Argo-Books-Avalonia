// Speaks a script with ElevenLabs and saves the audio with each character's timing, for captions that follow the words.
// Usage: node --env-file=.env.local elevenlabs_voice.mjs VOICE_ID SCRIPT.txt OUT.mp3 OUT.json
import { readFileSync, writeFileSync } from "fs";
import { ElevenLabsClient } from "@elevenlabs/elevenlabs-js";

const [voiceId, scriptPath, audioPath, timingPath] = process.argv.slice(2);
const client = new ElevenLabsClient();
const result = await client.textToSpeech.convertWithTimestamps(voiceId, {
  text: readFileSync(scriptPath, "utf-8").trim(),
  modelId: "eleven_v4",
});
writeFileSync(audioPath, Buffer.from(result.audioBase64, "base64"));
writeFileSync(timingPath, JSON.stringify(result.alignment));
