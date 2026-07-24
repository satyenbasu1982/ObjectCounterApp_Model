namespace ObjectCounterApp.Core
{
    internal readonly struct Box
    {
        public readonly float X1, Y1, X2, Y2, Score, PersonLikeScore;

        public Box(float cx, float cy, float w, float h, float score, float personLikeScore)
        {
            X1 = cx - w / 2f;
            Y1 = cy - h / 2f;
            X2 = cx + w / 2f;
            Y2 = cy + h / 2f;
            Score = score;
            PersonLikeScore = personLikeScore;
        }
    }
}
