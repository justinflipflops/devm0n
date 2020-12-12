using System;
namespace devm0n
{
    public class PollInterval
    {
        private TimeSpan internal_Minimum = new TimeSpan();
        private TimeSpan internal_Maximum = new TimeSpan();
        public TimeSpan Minimum {
            get 
            { 
                return internal_Minimum; 
            } 
            set
            {
                if (value > internal_Maximum)
                    internal_Maximum = value;
                internal_Minimum = value;
            }
        }
        public TimeSpan Maximum {
            get 
            { 
                return internal_Maximum; 
            } 
            set
            {
                if (value < internal_Minimum)
                    internal_Minimum = value;
                internal_Maximum = value;
            }
        }
        public TimeSpan Next()
        {
            return GetRandomTimeSpan(Minimum,Maximum);
        }
        private static Random _random = new Random();
        private TimeSpan GetRandomTimeSpan(TimeSpan LowerBound, TimeSpan UpperBound)
        {
            if (LowerBound == null || UpperBound == null)
                throw new ArgumentNullException();
            if (LowerBound > UpperBound)
                throw new ArgumentOutOfRangeException();
            double _lower = LowerBound.TotalMilliseconds;
            double _upper = UpperBound.TotalMilliseconds;
            double _result = _random.NextDouble(_lower,_upper);
            return TimeSpan.FromMilliseconds(_result);
        }
    }
}