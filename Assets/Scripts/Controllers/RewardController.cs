using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.Audio;
using UnityEngine.InputSystem;
using UnityEngine.UI;

public class RewardController : MonoBehaviour
{
    private enum Phase { None, ChooseFace, ChooseDie, Inspect }

    [Header("Input Actions")]
    [SerializeField] private InputActionReference select;    // LMB
    [SerializeField] private InputActionReference rotateM;   // RMB (hold)
    [SerializeField] private InputActionReference mousePos;  // Pointer position
    [SerializeField] private InputActionReference rotateK;   // 1D Axis: Q = -1, E = +1

    [Header("References")]
    [SerializeField] private Camera cam;
    [SerializeField] private Transform inspectPoint;         // child of the camera
    [SerializeField] private TMP_Text promptText;
    [SerializeField] private StripDisplay strip;
    [SerializeField] private Color promptColor = new Color32(0xFF, 0x71, 0x34, 0xFF);
    [SerializeField] private FaceView numeralPreview;

    [Header("Feel")]
    [SerializeField] private float moveTime = 0.4f;
    [SerializeField] private float snapTime = 0.15f;
    [SerializeField] private float dragSpeed = 0.3f;
    [SerializeField] private float afterSwapPause = 0.3f;
    [SerializeField] private float hoverScale = 1.15f;       // how much things grow when hovered

    [Header("Glow")]
    [SerializeField, ColorUsage(true, true)] private Color glowColor = new Color(1f, 0.6f, 0.2f) * 1.5f;
    [SerializeField] private float pulseSpeed = 4f;

    [Header("Views")]
    [SerializeField] private CameraRig rig;
    [SerializeField] private CameraController cameraController;
    [SerializeField] private Transform tableView;
    [SerializeField] private Transform monitorView;
    [SerializeField] private Transform diceView;

    [Header("Screen")]
    [SerializeField] private Collider screenCollider;     // MeshCollider on the screen mesh
    [SerializeField] private GameObject rewardPanel;      // on the ScreenCanvas, holds the 3 images
    [SerializeField] private Image[] offerImages;         // left, middle, right
    [SerializeField] private GameObject equationPanel;    // the equation texts, hidden during the choice
    [SerializeField] private Color offerColor = new Color32(0xFF, 0x71, 0x34, 0xFF);
    [SerializeField] private Color dimColor = new Color32(0xFF, 0x71, 0x34, 0x40);

    [Header("Chosen Face")]
    [SerializeField] private Transform chosenFacePoint;   // child of the camera, bottom corner
    [SerializeField] private float chosenFaceScale = 0.5f;
    [SerializeField] private float chosenFaceSpin = 45f;  // degrees per second

    [Header("Offers")]
    [SerializeField] private Sprite healIcon;
    [SerializeField] private Sprite growIcon;             // Reward_Grow
    [SerializeField, Range(0f, 1f)] private float growChance = 0.35f;   // how often Grow takes the special slot (it used to show up almost every time)
    [SerializeField] private Sprite keepIcon;             // "keep your die" on the spare swap screen
    [SerializeField] private TMP_Text offerDescription;   // one big text under the row of offers

    [Header("The New Slot")]   // every screen: one face, one special (heal or Grow), one of these
    [SerializeField] private FaceDefinition[] numberFaces;   // 10 slots, slot N = the face for N (slot 0 can stay empty): Gamble, Pair and Polish use them
    [SerializeField] private ChipTray chipTray;
    [SerializeField] private ChipDefinition[] chipPool;      // the chips a reward can hand out
    [SerializeField] private Sprite gambleIcon;
    [SerializeField] private Sprite pairIcon;
    [SerializeField] private Sprite polishIcon;
    [SerializeField] private Sprite maxHpIcon;
    [SerializeField] private bool offerChips = true, offerGamble = true, offerPair = true, offerPolish = true, offerMaxHp = true;
    [SerializeField] private int pairNumber = 5;             // Pair: this number on two different dice
    [SerializeField, Range(5, 50)] private int maxHpPercent = 20;
    private int MaxHpPercent => Mathf.Max(1, Mathf.RoundToInt(maxHpPercent * RunSettings.RaiseMaxHpRewardMultiplier));   // Raise (Skimmed)
    [SerializeField, Range(0f, 1f)] private float multiplyChance = 0.5f;   // once you own a ×, the face is a × this often (while a die has room)

    [Header("Gamble Sound")]   // the nixie's tick and lock clips work well here
    [SerializeField] private AudioClip gambleTickClip;       // each number the reel flicks past
    [SerializeField] private AudioClip gambleLandClip;       // the number it stops on
    [SerializeField] private AudioMixerGroup sfxOutput;      // your SFX group, so the volume slider covers it
    [SerializeField, Range(0f, 1f)] private float gambleVolume = 0.7f;

    [Header("Relic Reveal")]
    [SerializeField] private float relicRevealTime = 2.2f;   // the picked relic stays big on the screen this long
    private string screenPrompt = "PICK A REWARD";        // what the sign says when nothing is hovered
    private Die switchTo;
    private RewardOffer chosen;

    private FaceView chosenFaceView;

    private Phase phase = Phase.None;

    // Phase A: choose a reward
    private List<RewardOffer> offers;
    private FaceDefinition chosenFace;

    // Phase B: choose a die
    private Die[] candidates;
    private string pickPrompt;  

    // Phase C: inspect + pick a side
    private Die die;
    private Transform homeParent;
    private Vector3 homePos;
    private Quaternion homeRot;
    private bool isSnapping;
    private int selectedIndex = -1;
    private bool confirmed;
    private bool numbersOnly;          // growing: only number sides can be copied
    private Vector2 lastMousePos;

    // Hover
    private Transform hovered;
    private Vector3 hoveredScale;

    private MaterialPropertyBlock glowBlock;
    private string rewardNote;   // what a face went over, or how a die grew, for the sheet
    private bool xWasCapped;   // a × was in the pool, but every die is full


    // ---------- Entry point ----------

    // GameController does: yield return reward.RewardRoutine(player, facePool.ToArray(), beaten.unlockFaces, heal, relicChoices);
    // relicChoices is null except after the Relic Fights (GameController): then a relic screen follows the reward.
    public IEnumerator RewardRoutine(PlayerController player, FaceDefinition[] facePool, FaceDefinition[] newFaces, int healAmount,
                                     List<RelicDefinition> relicChoices = null)
    {
        cameraController.LookEnabled = false;

        offers = BuildOffers(player, facePool, newFaces, healAmount);
        yield return rig.MoveTo(monitorView);
        if (xWasCapped) strip.Flash("× FULL · NO ROOM FOR MORE ON YOUR DICE", promptColor, 3f);
        yield return ChooseOnScreen("PICK A REWARD");
        List<string> offered = OfferNames();
        RewardOffer pick = chosen;
        rewardNote = "";

        switch (chosen.kind)
        {
            case RewardKind.Face:
                chosenFace = chosen.face;
                yield return FaceRewardRoutine(player);
                break;

            case RewardKind.Heal:
                player.Heal(chosen.heal);
                yield return new WaitForSeconds(1f);   // let the nixies flicker up
                break;

            case RewardKind.Grow:
                yield return GrowRewardRoutine(player);
                break;

            case RewardKind.Die:                        // not offered any more (bosses hand their die over), kept just in case
                player.AddDie(chosen.die);
                yield return new WaitForSeconds(1f);
                break;

            case RewardKind.Gamble:                     // the number spins on the screen, then goes on a die like any face
                int rolled = Random.Range(1, 10);
                chosenFace = NumberFace(rolled);
                yield return GambleRoll(rolled);
                yield return FaceRewardRoutine(player);
                rewardNote = "rolled " + rolled + ", " + rewardNote;
                break;

            case RewardKind.Pair:                       // the same number on two different dice
                chosenFace = NumberFace(pairNumber);
                Die first = null;
                yield return FaceRewardRoutine(player, null, d => first = d);
                string firstNote = rewardNote;
                if (first != null) yield return FaceRewardRoutine(player, first);
                rewardNote = firstNote + " + " + rewardNote;
                break;

            case RewardKind.Polish:
                yield return PolishRoutine(player);
                break;

            case RewardKind.MaxHp:
                player.AddMaxHpPercent(MaxHpPercent);
                strip.Flash($"MAX HP {player.MaxHealth}", promptColor, 1.5f);
                yield return new WaitForSeconds(1f);
                break;

            case RewardKind.Chip:
                chipTray.Add(chosen.chip);
                strip.FlashScrolling($"{chosen.chip.displayName.ToUpper()} CHIP · HOLD S TO SEE YOUR CHIPS", promptColor, 2.5f);
                yield return new WaitForSeconds(1f);
                break;
        }

        Telemetry.Choice("reward", offered, OfferName(pick), rewardNote);

        if (relicChoices != null && relicChoices.Count > 0) yield return RelicPick(player, relicChoices);

        yield return rig.MoveTo(tableView);
        cameraController.LookEnabled = true;
    }

    // Shows 'offers' on the dot screen and waits for a click. Leaves the pick in 'chosen'.
    private IEnumerator ChooseOnScreen(string prompt)
    {
        screenPrompt = prompt;
        ShowOffers(true);
        chosen = null;
        SetPrompt(promptText, prompt);
        phase = Phase.ChooseFace;
        yield return new WaitUntil(() => chosen != null);
        ShowOffers(false);
        SetPrompt(promptText, "");
        SetPrompt(offerDescription, "");
    }

    // ---------- Relics ----------

    // Pick 1 of the relics GameController rolled (3 you don't have yet)
    private IEnumerator RelicPick(PlayerController player, List<RelicDefinition> choices)
    {
        yield return rig.MoveTo(monitorView);   // a face reward leaves the camera on the dice
        offers = new List<RewardOffer>();
        foreach (RelicDefinition r in choices)
        {
            offers.Add(new RewardOffer
            {
                kind = RewardKind.Relic,
                relic = r,
                icon = r.icon,
                label = r.displayName.ToUpper(),
                description = r.line.ToUpper()
            });
        }
        strip.Show("RELICS LAST ALL RUN", promptColor);   // so it doesn't read as a second reward screen
        yield return ChooseOnScreen("PICK A RELIC");
        Telemetry.Choice("relic", OfferNames(), OfferName(chosen));
        strip.Clear();

        RelicDefinition relic = chosen.relic;
        player.AddRelic(relic);   // the relic bar pops the new icon and plays the relic sound
        strip.Flash($"NEW RELIC · {relic.displayName.ToUpper()}", promptColor, relicRevealTime);
        yield return Reveal(relic.icon, relic.displayName.ToUpper(), relic.line.ToUpper(), relicRevealTime);
    }

    // The thing you just got, alone and big in the middle of the screen, with what it does on the sign
    private IEnumerator Reveal(Sprite icon, string text, string description, float time)
    {
        if (offerImages.Length == 0) yield break;
        rewardPanel.SetActive(true);
        equationPanel.SetActive(false);
        foreach (Image image in offerImages) image.gameObject.SetActive(false);

        Image middle = offerImages[offerImages.Length / 2];
        middle.sprite = icon;
        middle.color = offerColor;
        middle.gameObject.SetActive(true);
        SetPrompt(promptText, text);
        SetPrompt(offerDescription, description);

        Transform t = middle.transform;
        Vector3 size = t.localScale;
        const float popTime = 0.35f;
        for (float e = 0f; e < popTime; e += Time.deltaTime)
        {
            float k = e / popTime;
            t.localScale = size * (1f + 0.6f * Mathf.Sin(k * Mathf.PI) * (1f - k * 0.5f));   // swells, then settles
            yield return null;
        }
        t.localScale = size;
        yield return new WaitForSeconds(Mathf.Max(0f, time - popTime));

        middle.gameObject.SetActive(false);
        rewardPanel.SetActive(false);
        equationPanel.SetActive(true);
        SetPrompt(promptText, "");
        SetPrompt(offerDescription, "");
    }

    // The Gamble: the screen flicks through numbers like a slot reel, slows down, and lands on the roll
    private IEnumerator GambleRoll(int result)
    {
        if (offerImages.Length == 0) yield break;
        rewardPanel.SetActive(true);
        equationPanel.SetActive(false);
        foreach (Image image in offerImages) image.gameObject.SetActive(false);
        Image middle = offerImages[offerImages.Length / 2];
        middle.color = offerColor;
        middle.gameObject.SetActive(true);
        SetPrompt(promptText, "THE GAMBLE");
        SetPrompt(offerDescription, "");

        int shown = Random.Range(1, 10);
        for (float step = 0.04f; step < 0.25f; step *= 1.25f)   // about a second, slowing down
        {
            shown = 1 + (shown + Random.Range(0, 8)) % 9;          // never the same number twice in a row
            FaceDefinition f = NumberFace(shown);
            if (f != null) middle.sprite = f.icon;
            // a wheel slowing down: each click a little lower than the last
            PlayGamble(gambleTickClip, Mathf.Lerp(1.25f, 0.85f, Mathf.InverseLerp(0.04f, 0.25f, step)), 0.6f);
            yield return new WaitForSeconds(step);
        }
        PlayGamble(gambleLandClip, 1f, 1f);
        yield return Reveal(chosenFace.icon, $"YOU ROLLED A {result}", "NOW PUT IT ON A DIE", 1.4f);
    }

    private AudioSource[] gambleVoices;   // its own, so changing the pitch never bends another sound
    private int nextGambleVoice;

    private void PlayGamble(AudioClip clip, float pitch, float loudness)
    {
        if (clip == null) return;
        if (gambleVoices == null)
        {
            gambleVoices = new AudioSource[2];
            for (int i = 0; i < gambleVoices.Length; i++)
            {
                gambleVoices[i] = gameObject.AddComponent<AudioSource>();
                gambleVoices[i].playOnAwake = false;
                gambleVoices[i].spatialBlend = 0f;
                gambleVoices[i].outputAudioMixerGroup = sfxOutput;
            }
        }
        AudioSource voice = gambleVoices[nextGambleVoice];
        nextGambleVoice = (nextGambleVoice + 1) % gambleVoices.Length;
        voice.pitch = pitch;
        voice.PlayOneShot(clip, loudness * gambleVolume);
    }

    // ---------- Spare dice ----------

    // Before a Low fight: swap the spare in for your operator die. Before a Low or Target fight, with the Pocket Die
    // relic: the Pocket Die in for one of your number dice. PlayerController undoes both when the fight ends.
    // No spare at Target tables: it can't be upgraded, and - - ÷ ÷ + + is built for getting near 0, not for hitting 437.
    public IEnumerator SwapRoutine(PlayerController player, TableType table)
    {
        bool offerSpare = player.HasSpare && table == TableType.Low;
        bool offerPocket = player.HasPocket;
        if (!offerSpare && !offerPocket) yield break;
        cameraController.LookEnabled = false;
        yield return rig.MoveTo(monitorView);

        if (offerSpare)
        {
            offers = new List<RewardOffer>
            {
                new RewardOffer { kind = RewardKind.Keep, icon = keepIcon, label = "KEEP YOUR DIE", description = "PLAY THIS FIGHT WITH YOUR OWN DIE" },
                new RewardOffer { kind = RewardKind.Swap, icon = player.SpareDefinition.icon, label = "SPARE: " + FaceList(player.SpareDefinition),
                                  description = "BETTER FOR GETTING CLOSE TO 0" },
            };
            Tutorial.Hint("lowtable", "LOW TABLE: CLOSEST TO 0 WINS · 2 BEATS 9");
            yield return ChooseOnScreen("SWAP IN YOUR SPARE?");
            Telemetry.Choice("spare", OfferNames(), OfferName(chosen));
            if (chosen.kind == RewardKind.Swap) player.SwapInSpare();
        }

        if (offerPocket)
        {
            offers = new List<RewardOffer>
            {
                new RewardOffer { kind = RewardKind.Keep, icon = keepIcon, label = "KEEP YOUR DICE", description = "PLAY THIS FIGHT WITH YOUR OWN DICE" },
                new RewardOffer { kind = RewardKind.Swap, icon = player.PocketDefinition.icon, label = "POCKET DIE: " + FaceList(player.PocketDefinition),
                                  description = "SWAP A NUMBER DIE FOR SMALL NUMBERS THIS FIGHT" },
            };
            yield return ChooseOnScreen("USE THE POCKET DIE?");
            List<string> pocketOffers = OfferNames();
            string pocketPick = OfferName(chosen);
            string swappedOut = "";
            if (chosen.kind == RewardKind.Swap)
            {
                yield return rig.MoveTo(tableView);   // your dice are lying on the table now, not in the slots
                yield return PickDie(player.NumberDiceInPlay().ToArray(), "PICK A DIE TO SWAP OUT");
                Die picked = die;
                die = null;
                strip.Clear();
                swappedOut = Telemetry.Name(picked);
                player.SwapInPocket(picked);
            }
            Telemetry.Choice("pocket", pocketOffers, pocketPick, swappedOut);
        }

        yield return rig.MoveTo(tableView);
        cameraController.LookEnabled = true;
    }

    private static string FaceList(DieDefinition def)
    {
        string text = "";
        foreach (FaceDefinition f in def.faceDefinitions)
        {
            if (f == null) continue;
            text += (f.type == FaceType.Number ? f.number.ToString() : GameController.OpSymbol(f.op)) + " ";
        }
        return text.TrimEnd();
    }

    // New face: pick a die, pick a side, the new face slams onto it. A × only goes on a die under its × cap.
    // exclude: a die that can't be picked (Pair's second face). picked: told which die got the face.
    public IEnumerator FaceRewardRoutine(PlayerController player, Die exclude = null, System.Action<Die> picked = null)
    {
        List<Die> choices = new List<Die>();
        foreach (Die d in player.GetDice())
        {
            if (d == exclude) continue;
            if (!IsMultiply(chosenFace) || player.HasMultiplyRoom(d)) choices.Add(d);
        }
        if (choices.Count == 0) yield break;   // can't happen: × is only offered while a die has room

        yield return rig.MoveTo(diceView);
        ShowChosenFace();
        string prompt = exclude != null ? $"NOW A DIFFERENT DIE FOR THE OTHER {chosenFace.number}"
                      : IsMultiply(chosenFace) ? "PICK A DIE WITH ROOM FOR A ×" : "PICK A DIE";
        yield return PickDie(choices.ToArray(), prompt);
        ClearChosenFace();

        yield return PickSide("PICK A SIDE", "RIGHT DRAG OR Q/E TO TURN · CLICK A SIDE TWICE TO PICK IT", false);
        FaceDefinition old = die.GetCurrentFaces()[selectedIndex];
        rewardNote = $"{(old == null ? "blank" : Telemetry.Name(old))} on {(PlayerController.IsNumberDie(die) ? "number" : "operator")} die";

        // Swap
        yield return die.SwapFace(selectedIndex, chosenFace);
        player.NotifyDiceChanged();
        yield return new WaitForSeconds(afterSwapPause);

        // Back to where it was
        die.transform.SetParent(homeParent, true);
        yield return MoveDieWorld(homePos, homeRot);

        picked?.Invoke(die);
        die = null;
    }

    // Polish: pick a number die, every number on it goes up by 1 (a 9 stays a 9)
    private IEnumerator PolishRoutine(PlayerController player)
    {
        List<Die> numberDice = player.NumberDiceInPlay();
        if (numberDice.Count == 0) yield break;

        yield return rig.MoveTo(diceView);
        yield return PickDie(numberDice.ToArray(), "PICK A DIE TO POLISH · EVERY NUMBER +1");
        strip.Clear();
        FaceDefinition[] faces = die.GetCurrentFaces();
        int raised = 0;
        for (int i = 0; i < faces.Length; i++)
        {
            FaceDefinition f = faces[i];
            if (f == null || f.type != FaceType.Number || f.number >= 9) continue;
            FaceDefinition up = NumberFace(f.number + 1);
            if (up == null) continue;
            StartCoroutine(die.SwapFace(i, up));   // all at once: six slams in a row would drag
            raised++;
        }
        yield return new WaitForSeconds(afterSwapPause + 0.6f);
        player.NotifyDiceChanged();
        rewardNote = $"{raised} faces +1";
        strip.FlashScrolling($"POLISHED · {raised} NUMBERS WENT UP", promptColor, 1.5f);
        die = null;
        yield return new WaitForSeconds(1f);
    }

    // Gamble and Polish need every face from 1 to 9 in Number Faces
    private bool HasNumberFaces()
    {
        for (int n = 1; n <= 9; n++) if (NumberFace(n) == null) return false;
        return true;
    }

    private FaceDefinition NumberFace(int n)
    {
        if (numberFaces == null || n < 0 || n >= numberFaces.Length) return null;
        return numberFaces[n];
    }

    // Grow a die: pick a number die, pick one of its numbers, it comes back one size up with copies of that number
    public IEnumerator GrowRewardRoutine(PlayerController player)
    {
        List<Die> growable = new List<Die>();
        foreach (Die d in player.GetDice())
        {
            if (player.NextShape(d) != null) growable.Add(d);
        }
        if (growable.Count == 0) yield break;    // BuildOffers only offers this when something can grow

        yield return rig.MoveTo(diceView);
        yield return PickDie(growable.ToArray(), "PICK A DIE TO GROW");
        yield return PickSide("PICK A NUMBER FOR THE NEW SIDE", "RIGHT DRAG OR Q/E TO TURN · CLICK A NUMBER TWICE · ONE NEW SIDE COPIES IT", true);

        // Back to where it was, then swap it for the bigger one
        die.transform.SetParent(homeParent, true);
        yield return MoveDieWorld(homePos, homeRot);

        string newShape = player.NextShape(die).displayName;
        rewardNote = $"{die.GetCurrentFaces().Length} sides to {newShape}, copied {Telemetry.Name(die.GetCurrentFaces()[selectedIndex])}";
        player.GrowDie(die, selectedIndex);
        die = null;

        strip.Flash(string.IsNullOrEmpty(newShape) ? "YOUR DIE GREW!" : $"IT GREW INTO A {newShape.ToUpper()}!", promptColor, 2f);
        yield return new WaitForSeconds(1.5f);   // watch it drop in
    }
    private IEnumerator WarnFull(Die full)
    {
        strip.Flash("THAT DIE IS FULL OF ×", Color.red, 1.5f);
        Vector3 home = full.transform.position;
        for (float t = 0f; t < 0.3f; t += Time.deltaTime)
        {
            full.transform.position = home + cam.transform.right * Mathf.Sin(t * 60f) * 0.05f * (1f - t / 0.3f);
            yield return null;
        }
        full.transform.position = home;
        yield return new WaitForSeconds(1.2f);
        if (phase == Phase.ChooseDie) strip.ShowScrolling(pickPrompt, promptColor, -1);   // still picking: the instruction comes back
    }

    // ---------- Shared steps ----------

    // B. Click one of these dice. Leaves it in 'die'.
    private IEnumerator PickDie(Die[] choices, string prompt)
    {
        pickPrompt = prompt;
        candidates = choices;
        die = null;
        strip.ShowScrolling(prompt, promptColor, -1);   // a long prompt scrolls
        phase = Phase.ChooseDie;
        yield return new WaitUntil(() => die != null);
        phase = Phase.None;
        ClearHover();
    }

    // C. Bring 'die' up close and wait until a side is clicked twice (selectedIndex).
    //    Clicking another candidate puts this one back and brings that one up.
    private IEnumerator PickSide(string prompt, string hint, bool onlyNumbers)
    {
        numbersOnly = onlyNumbers;
        while (true)
        {
            die.GetComponent<DiceController>().ToggleInputs(false);
            homeParent = die.transform.parent;
            homePos = die.transform.position;
            homeRot = die.transform.rotation;
            die.transform.SetParent(inspectPoint, true);
            yield return MoveDieLocal(Vector3.zero);

            selectedIndex = -1;
            confirmed = false;
            switchTo = null;
            lastMousePos = mousePos.action.ReadValue<Vector2>();
            strip.ShowScrolling(prompt, promptColor, -1);   // a long prompt scrolls
            Tutorial.Hint("side", hint);
            phase = Phase.Inspect;
            yield return new WaitUntil(() => confirmed || switchTo != null);
            phase = Phase.None;
            ClearHover();
            if (confirmed) break;

            if (selectedIndex != -1) SetGlow(die.GetFaceView(selectedIndex), 0f);
            die.transform.SetParent(homeParent, true);
            yield return MoveDieWorld(homePos, homeRot);
            die = switchTo;
        }
        strip.Clear();
        Tutorial.Done("side");
    }

    // ---------- Input ----------

    private void OnEnable()
    {
        select.action.performed += OnSelect;
        rotateK.action.performed += OnRotateKey;
    }

    private void OnDisable()
    {
        select.action.performed -= OnSelect;
        rotateK.action.performed -= OnRotateKey;
    }

    private void Update()
    {
        switch (phase)
        {
            case Phase.ChooseFace:
                HighlightOffer(RaycastScreenOption());
                break;

            case Phase.ChooseDie:
                Die hoverDie = RaycastPlayerDie();
                UpdateHover(hoverDie != null ? hoverDie.transform : null);
                if (chosenFaceView != null)
                {
                    chosenFaceView.transform.Rotate(Vector3.up, chosenFaceSpin * Time.deltaTime, Space.Self);
                }
                break;

            case Phase.Inspect:
                UpdateInspect();
                Die other = RaycastPlayerDie();
                UpdateHover(other != null && other != die ? other.transform : null);
                break;
        }
    }

    private void OnSelect(InputAction.CallbackContext context)
    {
        switch (phase)
        {
            case Phase.ChooseFace:
                int option = RaycastScreenOption();
                if (option < 0) return;
                chosen = offers[option];
                phase = Phase.None;
                break;

            case Phase.ChooseDie:
                Die clicked = RaycastPlayerDie();
                if (clicked == null)
                {
                    Die full = RaycastAnyDie();                         // a die, just not one you can pick
                    if (full != null && IsMultiply(chosenFace)) StartCoroutine(WarnFull(full));
                    return;
                }
                ClearHover();
                die = clicked;
                phase = Phase.None;
                break;

            case Phase.Inspect:
                SelectSide();
                break;
        }
    }

    private void OnRotateKey(InputAction.CallbackContext context)
    {
        if (phase != Phase.Inspect || isSnapping) return;
        float dir = context.ReadValue<float>();   // -1 or +1
        Quaternion target = Quaternion.AngleAxis(90f * dir, cam.transform.up) * die.transform.rotation;
        StartCoroutine(SnapRotate(target));
    }

    // ---------- Phase A: offers ----------

    // Every screen: one face, one special (a heal, or Grow), and one new thing (a chip, Gamble, Pair, Polish or Max HP)
    private List<RewardOffer> BuildOffers(PlayerController player, FaceDefinition[] facePool, FaceDefinition[] newFaces, int healAmount)
    {
        List<RewardOffer> result = new List<RewardOffer>();
        List<FaceDefinition> bag = new List<FaceDefinition>(facePool);

        // × cap: once your operator die holds all the × it can, × stops showing up (even an enemy's new ×)
        xWasCapped = !player.OperatorDieHasRoom() && bag.Exists(IsMultiply);
        if (xWasCapped) bag.RemoveAll(IsMultiply);

        // 1. The face: one that's new to the pool (this enemy just unlocked it) first, then × while you have none (pity),
        //    then × some of the time (it used to get two face slots), else a random one
        FaceDefinition face = null;
        if (newFaces != null)
        {
            List<FaceDefinition> before = new List<FaceDefinition>(facePool);
            foreach (FaceDefinition f in newFaces) before.Remove(f);
            foreach (FaceDefinition f in newFaces)
                if (bag.Contains(f) && !before.Contains(f)) { face = f; break; }
        }
        if (face == null && bag.Exists(IsMultiply) && (!player.HasAnyMultiply() || Random.value < multiplyChance))
            face = bag.Find(IsMultiply);
        if (face == null) face = TakeRandom(bag, new List<FaceDefinition>());
        if (face != null) result.Add(FaceOffer(face));

        // 2. The special: a heal at half HP or less (or when nothing can grow), Grow at full HP, else either
        bool low = player.Health * 2 <= player.MaxHealth;
        bool full = player.Health >= player.MaxHealth;
        if (!low && player.CanGrowAny() && (full || Random.value < growChance))
        {
            result.Add(new RewardOffer
            {
                kind = RewardKind.Grow,
                icon = growIcon,
                label = "GROW A DIE",
                description = "ONE DIE GETS MORE SIDES, COPYING A NUMBER YOU PICK"
            });
        }
        else result.Add(HealOffer(healAmount));

        // 3. The new thing
        RewardOffer extra = NewOffer(player);
        if (extra != null) result.Add(extra);

        // Shuffle, so the special isn't always in the same spot
        for (int i = 0; i < result.Count; i++)
        {
            int j = Random.Range(i, result.Count);
            RewardOffer temp = result[i];
            result[i] = result[j];
            result[j] = temp;
        }
        return result;
    }

    private RewardOffer NewOffer(PlayerController player)
    {
        List<RewardKind> kinds = new List<RewardKind>();
        if (offerChips && chipTray != null && chipTray.HasRoom && chipPool != null && chipPool.Length > 0) kinds.Add(RewardKind.Chip);
        if (offerGamble && HasNumberFaces()) kinds.Add(RewardKind.Gamble);
        if (offerPair && NumberFace(pairNumber) != null && player.NumberDiceInPlay().Count >= 2) kinds.Add(RewardKind.Pair);
        if (offerPolish && HasNumberFaces()) kinds.Add(RewardKind.Polish);
        if (offerMaxHp) kinds.Add(RewardKind.MaxHp);
        if (kinds.Count == 0) return null;

        switch (kinds[Random.Range(0, kinds.Count)])
        {
            case RewardKind.Chip:
                ChipDefinition chip = chipPool[Random.Range(0, chipPool.Length)];
                return new RewardOffer { kind = RewardKind.Chip, chip = chip, icon = chip.icon,
                                         label = chip.displayName.ToUpper() + " CHIP",
                                         description = chip.line.ToUpper() + " · ONE USE, ON YOUR TURN" };
            case RewardKind.Gamble:
                return new RewardOffer { kind = RewardKind.Gamble, icon = gambleIcon, label = "THE GAMBLE",
                                         description = "A NUMBER FROM 1 TO 9, ROLLED WHEN YOU TAKE IT" };
            case RewardKind.Pair:
                return new RewardOffer { kind = RewardKind.Pair, icon = pairIcon, label = $"A PAIR OF {pairNumber}S",
                                         description = $"A {pairNumber} ON TWO DIFFERENT DICE" };
            case RewardKind.Polish:
                return new RewardOffer { kind = RewardKind.Polish, icon = polishIcon, label = "POLISH",
                                         description = "EVERY NUMBER ON ONE DIE GOES UP BY 1" };
            default:
                return new RewardOffer { kind = RewardKind.MaxHp, icon = maxHpIcon, label = $"+{MaxHpPercent}% MAX HP",
                                         description = "MORE MAX HP FOR THE REST OF THE RUN, AND THAT MUCH HP NOW" };
        }
    }

    // A random face from the bag that isn't already offered, or null if there's none left.
    // The pool holds duplicates (lots of 9s later on); this keeps two identical offers off the screen.
    private FaceDefinition TakeRandom(List<FaceDefinition> bag, List<FaceDefinition> alreadyOffered)
    {
        bag.RemoveAll(f => alreadyOffered.Contains(f));
        if (bag.Count == 0) return null;
        int r = Random.Range(0, bag.Count);
        FaceDefinition face = bag[r];
        bag.RemoveAt(r);
        return face;
    }

    private static bool IsMultiply(FaceDefinition f)
    {
        return f != null && f.type == FaceType.Operator && f.op == Operator.Multiply;
    }

    private RewardOffer FaceOffer(FaceDefinition f)
    {
        string what = f.type == FaceType.Number ? $"PUT A {f.number} ON ONE SIDE OF ANY DIE"
                    : IsMultiply(f) ? "MULTIPLY: 6 AND 7 MAKE 42 · A DIE HOLDS 1 × PER 3 SIDES"
                    : $"PUT A {GameController.OpSymbol(f.op)} ON ONE SIDE OF ANY DIE";
        return new RewardOffer { kind = RewardKind.Face, face = f, icon = f.icon, label = "NEW FACE", description = what };
    }
    private RewardOffer HealOffer(int amount)
    {
        return new RewardOffer
        {
            kind = RewardKind.Heal,
            heal = amount,
            icon = healIcon,
            label = "+" + amount + " HP",
            description = $"GET {amount} HP BACK"
        };

    }

    private void ShowOffers(bool on)
    {
        rewardPanel.SetActive(on);
        equationPanel.SetActive(!on);
        if (on)
        {
            for (int i = 0; i < offerImages.Length; i++) offerImages[i].gameObject.SetActive(false);
            for (int option = 0; option < offers.Count; option++)
            {
                int i = ImageFor(option);
                if (i >= offerImages.Length) continue;
                offerImages[i].sprite = offers[option].icon;
                offerImages[i].gameObject.SetActive(true);
            }
        }
    }

    private int RaycastScreenOption()
    {
        if (!MouseRaycast(out RaycastHit hit)) return -1;
        if (hit.collider != screenCollider) return -1;

        Vector2 uv = hit.textureCoord;
        float across = uv.x;                 // 0 at the left edge, 1 at the right
        int option = (int)(across * offers.Count);
        return Mathf.Clamp(option, 0, offers.Count - 1);
    }

    // Two choices (keep or swap) go in the outer images, so each sits inside its half of the screen
    private int ImageFor(int option)
    {
        if (offers.Count == 2 && offerImages.Length >= 3) return option == 0 ? 0 : offerImages.Length - 1;
        return option;
    }

    private void HighlightOffer(int hoveredOption)
    {
        for (int option = 0; option < offers.Count; option++)
        {
            int i = ImageFor(option);
            if (i < offerImages.Length)
                offerImages[i].color = (option == hoveredOption || hoveredOption == -1) ? offerColor : dimColor;
        }
        SetPrompt(promptText, hoveredOption >= 0 ? offers[hoveredOption].label : screenPrompt);
        SetPrompt(offerDescription, hoveredOption >= 0 ? offers[hoveredOption].description : "");

    }

    // ---------- Phase B: dice ----------

    private Die RaycastPlayerDie()
    {
        if (!MouseRaycast(out RaycastHit hit)) return null;
        Die hitDie = hit.collider.GetComponentInParent<Die>();
        if (hitDie == null || System.Array.IndexOf(candidates, hitDie) < 0) return null;   // only dice you can pick right now
        return hitDie;
    }
    private Die RaycastAnyDie()
    {
        if (!MouseRaycast(out RaycastHit hit)) return null;
        return hit.collider.GetComponentInParent<Die>();
    }
    private void ShowChosenFace()
    {
        FaceView newChosenFace;
        if (chosenFace.faceView != null) newChosenFace = Instantiate(chosenFace.faceView, chosenFacePoint);
        else
        {
            newChosenFace = Instantiate(numeralPreview, chosenFacePoint);
            newChosenFace.SetLabel(chosenFace.type == FaceType.Operator
                ? GameController.OpSymbol(chosenFace.op) : chosenFace.number.ToString());
        }
        newChosenFace.transform.localPosition = Vector3.zero;
        newChosenFace.transform.localScale *= chosenFaceScale;
        newChosenFace.transform.rotation = Quaternion.LookRotation(cam.transform.up, -cam.transform.forward);
        chosenFaceView = newChosenFace;
    }

    private void ClearChosenFace()
    {
        if (chosenFaceView != null) Destroy(chosenFaceView.gameObject);
        chosenFaceView = null;
    }

    // ---------- Phase C: inspect ----------

    private void UpdateInspect()
    {
        Vector2 mouse = mousePos.action.ReadValue<Vector2>();
        Vector2 delta = mouse - lastMousePos;
        lastMousePos = mouse;

        if (rotateM.action.IsPressed() && !isSnapping)
        {
            die.transform.Rotate(cam.transform.up, -delta.x * dragSpeed, Space.World);
            die.transform.Rotate(cam.transform.right, delta.y * dragSpeed, Space.World);
        }

        if (selectedIndex != -1)
        {
            float pulse = Mathf.Lerp(0.6f, 1.2f, (Mathf.Sin(Time.time * pulseSpeed) + 1f) * 0.5f);
            SetGlow(die.GetFaceView(selectedIndex), pulse);
        }
    }

    private void SelectSide()
    {
        if (!MouseRaycast(out RaycastHit hit)) return;                     // clicked nothing
        Die clicked = hit.collider.GetComponentInParent<Die>();
        if (clicked != die)
        {
            if (clicked != null && System.Array.IndexOf(candidates, clicked) >= 0) switchTo = clicked;
            return;
        }

        int index = die.GetSocketFacing(hit.normal);
        FaceDefinition picked = die.GetCurrentFaces()[index];
        if (numbersOnly && (picked == null || picked.type != FaceType.Number)) return;   // growing copies numbers only

        if (index == selectedIndex)
        {
            confirmed = true;                                              // second click on the same face = yes
            return;
        }

        if (selectedIndex != -1) SetGlow(die.GetFaceView(selectedIndex), 0f);   // old one off
        selectedIndex = index;                                                  // new one pulses in UpdateInspect
    }

    // ---------- Helpers ----------

    private bool MouseRaycast(out RaycastHit hit)
    {
        Ray ray = cam.ScreenPointToRay(mousePos.action.ReadValue<Vector2>());
        return Physics.Raycast(ray, out hit, 100f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);   // skips Ignore Raycast (the table rails)
    }

    private void UpdateHover(Transform target)
    {
        if (target == hovered) return;
        ClearHover();
        if (target == null) return;

        hovered = target;
        hoveredScale = target.localScale;
        target.localScale = hoveredScale * hoverScale;
    }

    private void ClearHover()
    {
        if (hovered != null) hovered.localScale = hoveredScale;
        hovered = null;
    }

    private void SetGlow(FaceView face, float intensity)
    {
        if (face != null) face.SetHighlight(intensity);
    }

    private void SetPrompt(TMP_Text textComponent, string text)
    {
        if (textComponent != null) textComponent.text = text;
    }

    // ---------- Movement ----------

    private IEnumerator MoveDieLocal(Vector3 targetLocal)
    {
        Vector3 start = die.transform.localPosition;
        float elapsed = 0f;

        while (elapsed < moveTime)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.SmoothStep(0f, 1f, elapsed / moveTime);
            die.transform.localPosition = Vector3.Lerp(start, targetLocal, t);
            yield return null;
        }
        die.transform.localPosition = targetLocal;
    }

    private IEnumerator MoveDieWorld(Vector3 targetPos, Quaternion targetRot)
    {
        Vector3 startPos = die.transform.position;
        Quaternion startRot = die.transform.rotation;
        float elapsed = 0f;

        while (elapsed < moveTime)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.SmoothStep(0f, 1f, elapsed / moveTime);
            die.transform.position = Vector3.Lerp(startPos, targetPos, t);
            die.transform.rotation = Quaternion.Slerp(startRot, targetRot, t);
            yield return null;
        }
        die.transform.position = targetPos;
        die.transform.rotation = targetRot;
    }

    private IEnumerator SnapRotate(Quaternion target)
    {
        isSnapping = true;
        Quaternion startRot = die.transform.rotation;
        float elapsed = 0f;

        while (elapsed < snapTime)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.SmoothStep(0f, 1f, elapsed / snapTime);
            die.transform.rotation = Quaternion.Slerp(startRot, target, t);
            yield return null;
        }
        die.transform.rotation = target;
        isSnapping = false;
    }

    public enum RewardKind { Face, Die, Heal, Grow, Relic, Keep, Swap, Chip, Gamble, Pair, Polish, MaxHp }

    public class RewardOffer
    {
        public RewardKind kind;
        public FaceDefinition face;   // kind == Face
        public DieDefinition die;     // kind == Die
        public int heal;              // kind == Heal
        public RelicDefinition relic; // kind == Relic
        public ChipDefinition chip;   // kind == Chip
        public Sprite icon;           // what the dot screen shows
        public string label;          // what the sign says when you hover it
        public string description;    // what the sign says when you hover it
    }

    // What an offer is called in the sheet
    private static string OfferName(RewardOffer o)
    {
        switch (o.kind)
        {
            case RewardKind.Face: return Telemetry.Name(o.face);
            case RewardKind.Die: return Telemetry.Name(o.die);
            case RewardKind.Relic: return Telemetry.Name(o.relic);
            case RewardKind.Chip: return "chip " + (o.chip != null ? o.chip.displayName.ToLower() : "");
            default: return o.kind.ToString().ToLower();   // heal, grow, keep, swap
        }
    }

    private List<string> OfferNames()
    {
        List<string> names = new List<string>();
        foreach (RewardOffer o in offers) names.Add(OfferName(o));
        return names;
    }

    // Act bosses: pick 1 of a few dice (GameController rolls them from the Boss Dice Pool). It joins your number dice.
    public IEnumerator BossDieRoutine(PlayerController player, List<DieDefinition> choices)
    {
        cameraController.LookEnabled = false;
        yield return rig.MoveTo(monitorView);

        offers = new List<RewardOffer>();
        foreach (DieDefinition d in choices)
            offers.Add(new RewardOffer { kind = RewardKind.Die, die = d, icon = d.icon, label = DieName(d), description = DieAbout(d) });
        yield return ChooseOnScreen("PICK YOUR NEW DIE");

        player.AddDie(chosen.die);
        Telemetry.Choice("bossdie", choices.ConvertAll(d => Telemetry.Name(d)), Telemetry.Name(chosen.die));
        yield return rig.MoveTo(tableView);   // watch it drop in
        cameraController.LookEnabled = true;
    }

    // The sign when you hover a boss die: its name on top, its line (or its faces, if it has no line) underneath
    private static string DieName(DieDefinition def) => (string.IsNullOrEmpty(def.displayName) ? def.name : def.displayName).ToUpper();
    private static string DieAbout(DieDefinition def) => (string.IsNullOrEmpty(def.line) ? FaceList(def) : def.line).ToUpper();
}