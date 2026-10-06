// Copyright (c) 2026 Arash Ghasemi Rad. All rights reserved.
// Licensed under the SqlPilot Source-Available Use License. See LICENSE.

using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using SqlPilot.Core;

namespace SqlPilot.Ssms
{
    public sealed class MatchLabel : TextBlock
    {
        public static readonly DependencyProperty CandidateProperty = DependencyProperty.Register(
            "Candidate", typeof(Candidate), typeof(MatchLabel), new PropertyMetadata(null, Changed));
        public Candidate Candidate
        {
            get => (Candidate)GetValue(CandidateProperty); set => SetValue(CandidateProperty, value);
        }
        static readonly Brush Accent = MakeAccent();
        static Brush MakeAccent()
        {
            var brush = new SolidColorBrush(Color.FromArgb(55, 240, 184, 65));
            brush.Freeze();
            return brush;
        }
        static void Changed(DependencyObject target, DependencyPropertyChangedEventArgs args)
        {
            var block = (MatchLabel)target;
            block.Inlines.Clear();
            var item = args.NewValue as Candidate;
            if (item == null)
                return;
            string label = item.Label ?? "";
            int start = 0;
            while (start < label.Length)
            {
                bool matched = Array.IndexOf(item.MatchIndices, start) >= 0;
                int end = start + 1;
                while (end < label.Length && (Array.IndexOf(item.MatchIndices, end) >= 0) == matched)
                    end++;
                var run = new Run(label.Substring(start, end - start));
                if (matched)
                {
                    run.Background = Accent;
                    run.FontWeight = FontWeights.SemiBold;
                }
                block.Inlines.Add(run);
                start = end;
            }
        }
    }
}
