﻿using System;
using System.Text;
using System.Threading;

namespace ConsoleInteractive {
    /*
     * The goal of this ConsoleBuffer is to create a non-interruptable, unlimited length user input buffer.
     * We use three variables to track the current input-
     * The CurrentBufferPos, which determines the current position in the StringBuffer the user is operating at
     * The ConsoleOutputBeginPos, which determines the starting position of the output in the StringBuffer
     * The ConsoleOutputLength, which determines the length of the string to be shown to the user.
     *
     * All these determine the following:
     *  abcdefg[hijklm|nopqrstuvwxyz]yxwvutsrqp - Input String
     *  0123456[78901234567890123456]7890123456 - Index Number
     *         ^      ^             ^
     *         |      |             |
     *         ConsoleOutputBeginPos|
     *                |             |
     *                CurrentBufferPos
     *                              |
     *                              ConsoleOutputLength
     * 
     * Given this example string, our variable states would be (zero-index):
     * CurrentBufferPos = 13
     * ConsoleOutputBeginPos = 6
     * ConsoleOutputLength = 19 (non-zero index)
     *
     * The contents of the square brackets will be shown to the user,
     * The I bar denominates the current input position,
     * The rest of the string remains untouched.
     *
     * Incrementing the ConsoleOutputBeginPos would move the output forward:
     * abcdefgh[ijklmnopqrstuvwxyzy]xwvutsrqp
     * - This requires us to check if we have hit the end of the string.
     * - The ConsoleOutputLength does not change, only the BeginPos.
     *
     * Decrementing the ConsoleOutputBeginPos would move the output backward:
     * abcdef[ghijklmnopqrstuvwxy]zyxwvutsrqp
     * - This requires us to check if we have hit the beginning of the string.
     * - The ConsoleOutputLength does not change, only the BeginPos.
     *
     * Incrementing the ConsoleOutputLength would move the farthest end of the output forward:
     * abcdefg[hijklmnopqrstuvwxyzy]xwvutsrqp
     * - This requires us to check if we have hit the end of the Console Width.
     * - Attempting to increment further than the Console Width does nothing. Increment the ConsoleOutputBeginPos instead.
     *
     * Decrementing the ConsoleOutputLength would move the farthest end of the output backward:
     * abcdefg[hijklmnopqrstuvwxy]zyxwvutsrqp
     * - Decrementing the ConsoleOutputLength is inadvisable.
     *
     */
    internal static class ConsoleBuffer {
        internal static StringBuilder UserInputBuffer = new();
        internal static volatile int CurrentBufferPos = 0;
        private static volatile int ConsoleOutputBeginPos = 0;
        private static volatile int ConsoleOutputLength = 0;
        private static volatile int ConsoleWriteLimit = InternalContext.CursorLeftPosLimit - 1;
        
        internal static void Insert(char c) {
            // Insert at the current buffer pos.
            UserInputBuffer.Insert(CurrentBufferPos, c);
            // Increment the buffer pos to reflect this change.
            Interlocked.Increment(ref CurrentBufferPos);
            // Increment the console cursor.
            InternalContext.IncrementLeftPos();

            // If we're at the end of the console (buffer pos is gt/eq to console horiz limit)
            if (InternalContext.CursorLeftPosLimit <= CurrentBufferPos)
                Interlocked.Increment(ref ConsoleOutputBeginPos);
            else
                Interlocked.Increment(ref ConsoleOutputLength);
            
            // Redraw the input.
            RedrawInput(InternalContext.CursorLeftPos);
        }

        internal static void RedrawInput(int leftCursorPosition) {
            Console.CursorVisible = false;
            ClearVisibleUserInput();
            Console.Write(UserInputBuffer.ToString().Substring(ConsoleOutputBeginPos, ConsoleOutputLength));
            InternalContext.SetLeftCursorPosition(leftCursorPosition);
            Console.CursorVisible = true;
        }

        // todo test
        internal static void MoveCursorForward() {
            if (CurrentBufferPos == UserInputBuffer.Length)
                return;
            
            Interlocked.Increment(ref CurrentBufferPos);
            InternalContext.IncrementLeftPos();
            // If we're at the end of the console (current buffer is gt/eq the write limit) 
            if (CurrentBufferPos > ConsoleWriteLimit) {
                // Increment ConsoleOutputBeginPos by one.
                ConsoleOutputBeginPos = Interlocked.Increment(ref ConsoleOutputBeginPos);
                Interlocked.Exchange(ref ConsoleOutputLength, ConsoleWriteLimit); 
                return;
            }
            
            DetermineCurrentInputPos();
            RedrawInput(InternalContext.CursorLeftPos);
        }

        // todo test
        internal static void MoveCursorBackward() {
            if (CurrentBufferPos == 0)
                return;
            
            CurrentBufferPos = Interlocked.Decrement(ref CurrentBufferPos);
            InternalContext.DecrementLeftPos();

            if (InternalContext.CursorLeftPos == 0) {
                if (ConsoleOutputBeginPos != 0) {
                    ConsoleOutputBeginPos = Interlocked.Decrement(ref ConsoleOutputBeginPos);
                    Interlocked.Exchange(ref ConsoleOutputLength, ConsoleWriteLimit - 1);
                }
            } else 
                ConsoleOutputBeginPos = Interlocked.Decrement(ref ConsoleOutputBeginPos);
            DetermineCurrentInputPos();
            RedrawInput(InternalContext.CursorLeftPos);
        }
        
        // todo test
        internal static void RemoveForward() {
            // If we're at the end of the buffer, do nothing.
            if (CurrentBufferPos >= UserInputBuffer.Length)
                return;
            
            // Remove 'forward', i.e. the delete button
            UserInputBuffer.Remove(CurrentBufferPos, 1);
            DetermineCurrentInputPos();
            RedrawInput(InternalContext.CursorLeftPos);
        }

        internal static void RemoveBackward() {
            // If we're at the start of the console, do nothing.
            if (CurrentBufferPos == 0)
                return;
            
            // Remove 'backward', i.e. backspace
            UserInputBuffer.Remove(CurrentBufferPos - 1, 1);
            Interlocked.Decrement(ref CurrentBufferPos);

            if (UserInputBuffer.Length < ConsoleWriteLimit) {
                InternalContext.DecrementLeftPos();
            }
            else
                Interlocked.Decrement(ref ConsoleOutputBeginPos);
            
            DetermineCurrentInputPos();
            RedrawInput(InternalContext.CursorLeftPos);
        }
        
        // todo cleanup
        private static void DetermineCurrentInputPos() {
            // If the current buffer length is 0
            if (UserInputBuffer.Length == 0) {
                Interlocked.Exchange(ref ConsoleOutputLength, 0);
                return;
            }
            
            // If we're at the beginning of the console
            if (UserInputBuffer.Length <= ConsoleWriteLimit) {
                // Set ConsoleOutputBeginPos to 0.
                Interlocked.Exchange(ref ConsoleOutputBeginPos, 0);
                Interlocked.Exchange(ref ConsoleOutputLength, UserInputBuffer.Length);
                return;
            }
        }

        internal static string FlushBuffer() {
            ClearVisibleUserInput();
            Interlocked.Exchange(ref CurrentBufferPos, 0);
            var retval = UserInputBuffer.ToString();
            UserInputBuffer.Clear();
            return retval;
        }

        /// <summary>
        /// Clears the visible user input but does not clear the internal buffer.
        /// </summary>
        internal static void ClearVisibleUserInput() {
            lock (InternalContext.WriteLock) {
                Console.SetCursorPosition(0, InternalContext.CursorTopPos);
                for (int i = 0; i <= ConsoleWriteLimit; i++) {
                    Console.Write(' ');
                }

                Console.SetCursorPosition(0, InternalContext.CursorTopPos);
                InternalContext.SetLeftCursorPosition(0);
            }
        }
    }
}