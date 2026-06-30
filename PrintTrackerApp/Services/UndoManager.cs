using System;
using System.Collections.Generic;

namespace PrintTrackerApp.Services
{
    public class UndoBatch
    {
        public List<Action> UndoActions { get; set; } = new List<Action>();
        public List<Action> RedoActions { get; set; } = new List<Action>();
    }

    public class UndoManager
    {
        private Stack<UndoBatch> _undoStack = new Stack<UndoBatch>();
        private Stack<UndoBatch> _redoStack = new Stack<UndoBatch>();
        
        public bool IsUndoingOrRedoing { get; private set; }

        public void AddBatch(UndoBatch batch)
        {
            if (batch == null || batch.UndoActions.Count == 0) return;
            _undoStack.Push(batch);
            _redoStack.Clear();
        }

        public void Undo()
        {
            if (_undoStack.Count == 0) return;
            IsUndoingOrRedoing = true;
            try
            {
                var batch = _undoStack.Pop();
                foreach (var action in batch.UndoActions)
                {
                    action();
                }
                _redoStack.Push(batch);
            }
            finally
            {
                IsUndoingOrRedoing = false;
            }
        }

        public void Redo()
        {
            if (_redoStack.Count == 0) return;
            IsUndoingOrRedoing = true;
            try
            {
                var batch = _redoStack.Pop();
                foreach (var action in batch.RedoActions)
                {
                    action();
                }
                _undoStack.Push(batch);
            }
            finally
            {
                IsUndoingOrRedoing = false;
            }
        }

        public void Clear()
        {
            _undoStack.Clear();
            _redoStack.Clear();
        }
    }
}
