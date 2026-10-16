using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace FishGame
{
    /// <summary>
    /// A fish can tag the task it's looking at (middle mouse or G): only that fish sees the tag - a marker on
    /// the task with how far off it is, held at the edge of the screen when it's out of view - for
    /// <see cref="Seconds"/>, from anywhere on the map. One tag at a time: tagging another task moves the tag
    /// there, tagging the same one again starts its time over; a task that's done drops its tag. Tasks: the
    /// reef puzzles, the tangled turtles and the trash sinkholes.
    ///
    /// Only on this machine, nothing is sent. <see cref="RoundScreen"/> ticks it and draws the markers.
    /// </summary>
    public static class TaskTags
    {
        /// <summary>How long a tag lasts.</summary>
        public const float Seconds = 40f;
        const float Reach = 50f;   // metres: as far off as a task can be tagged (it has to be seen)
        const float Aim = 15f;     // degrees off the middle of the screen it can be
        const float Close = 10f;   // metres: or this close, wherever you're looking

        public class Tag
        {
            public Object task;
            public string label;
            public float until;
        }

        static readonly List<Tag> s_tags = new List<Tag>();
        static readonly List<(Object task, string label)> s_options = new List<(Object, string)>();
        static InputAction s_tagAction;

        public static IReadOnlyList<Tag> Active => s_tags;

        /// <summary>Every frame, on this machine: tag on the key, drop tags that ran out or whose task is done.</summary>
        public static void Tick(FishPlayer me, Camera cam)
        {
            if (s_tagAction == null)
            {
                s_tagAction = new InputAction("TagTask", InputActionType.Button);
                s_tagAction.AddBinding("<Mouse>/middleButton");
                s_tagAction.AddBinding("<Keyboard>/g");
                s_tagAction.Enable();
            }

            // A fish in the water, playing - not the shark, not someone out of the round.
            bool fish = me != null && !me.IsSharkBody && !me.IsOut;
            if (!fish) { s_tags.Clear(); return; }
            if (cam != null && !me.IsHeld && !PauseMenu.IsOpen && !RoundScreen.ShowingResults && s_tagAction.WasPressedThisFrame())
                TagLookedAt(cam);
            s_tags.RemoveAll(t => Time.time > t.until || !Where(t.task, out _));
        }

        // The task nearest the middle of the screen (or, if none is, one right by you).
        static void TagLookedAt(Camera cam)
        {
            s_options.Clear();
            foreach (var puzzle in Object.FindObjectsByType<NetworkedSequenceTask>(FindObjectsInactive.Exclude))
                s_options.Add((puzzle, "Reef puzzle"));
            foreach (var turtle in TangledTurtle.All) s_options.Add((turtle, "Tangled turtle"));
            foreach (var hole in Sinkhole.All) s_options.Add((hole, "Trash sinkhole"));

            Vector3 eye = cam.transform.position, look = cam.transform.forward;
            Object best = null;
            string bestLabel = null;
            float bestScore = float.MaxValue;
            foreach (var (task, label) in s_options)
            {
                if (!Where(task, out Vector3 at)) continue;
                Vector3 to = at - eye;
                float distance = to.magnitude;
                if (distance > Reach) continue;
                float angle = Vector3.Angle(look, to);
                float score = angle <= Aim ? angle : distance <= Close ? Aim + distance : float.MaxValue;
                if (score < bestScore) { bestScore = score; best = task; bestLabel = label; }
            }
            if (best == null) return;

            // One tag at a time: a new one replaces the last (the same task again just starts its time over).
            s_tags.RemoveAll(t => t.task != best);
            if (s_tags.Count == 0) s_tags.Add(new Tag { task = best, label = bestLabel });
            s_tags[0].until = Time.time + Seconds;
        }

        /// <summary>Where a task's marker goes; false once it's done (or gone).</summary>
        public static bool Where(Object task, out Vector3 at)
        {
            switch (task)
            {
                case NetworkedSequenceTask puzzle when puzzle != null && !puzzle.Done:
                    at = puzzle.MarkerPoint;
                    return true;
                case TangledTurtle turtle when turtle != null && !turtle.Freed:
                    at = turtle.transform.position;
                    return true;
                case Sinkhole hole when hole != null && !hole.Complete:
                    at = hole.Centre;
                    return true;
            }
            at = default;
            return false;
        }
    }
}
