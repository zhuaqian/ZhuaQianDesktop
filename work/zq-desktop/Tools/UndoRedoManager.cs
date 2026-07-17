using System;
using System.Collections.Generic;

namespace ZhuaQianDesktopApp.Tools
{
    public enum UndoableActionType
    {
        OrganizeRollback,
        FileWrite,
        IndexChange,
        TaskDelete
    }

    public class UndoableAction
    {
        public UndoableActionType Type { get; set; }
        public string Label { get; set; }
        public DateTime Timestamp { get; set; }
        public object Data { get; set; }
    }

    public class UndoRedoManager
    {
        readonly Stack<UndoableAction> undoStack = new Stack<UndoableAction>();
        readonly Stack<UndoableAction> redoStack = new Stack<UndoableAction>();
        const int MaxUndo = 50;

        public int UndoCount { get { return undoStack.Count; } }
        public int RedoCount { get { return redoStack.Count; } }

        public event Action Changed;

        public void Record(UndoableActionType type, string label, object data)
        {
            undoStack.Push(new UndoableAction
            {
                Type = type,
                Label = label,
                Timestamp = DateTime.Now,
                Data = data
            });
            redoStack.Clear();
            if (undoStack.Count > MaxUndo)
            {
                var temp = new Stack<UndoableAction>();
                int keep = MaxUndo;
                foreach (var item in undoStack)
                {
                    if (keep-- > 0) temp.Push(item);
                }
                undoStack.Clear();
                foreach (var item in temp) undoStack.Push(item);
            }
            if (Changed != null) Changed();
        }

        public UndoableAction PeekUndo()
        {
            return undoStack.Count > 0 ? undoStack.Peek() : null;
        }

        public UndoableAction PeekRedo()
        {
            return redoStack.Count > 0 ? redoStack.Peek() : null;
        }

        public UndoableAction PopUndo()
        {
            if (undoStack.Count == 0) return null;
            var action = undoStack.Pop();
            redoStack.Push(action);
            if (Changed != null) Changed();
            return action;
        }

        public UndoableAction PopRedo()
        {
            if (redoStack.Count == 0) return null;
            var action = redoStack.Pop();
            undoStack.Push(action);
            if (Changed != null) Changed();
            return action;
        }

        public void Clear()
        {
            undoStack.Clear();
            redoStack.Clear();
            if (Changed != null) Changed();
        }
    }
}
