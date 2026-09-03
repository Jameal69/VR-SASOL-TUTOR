import os
import numpy as np

#you have to set the files files side by side if its not the standard layout (vr-sasl-tutor/backend-api/  and   vr-sasl-tutor/ml-service/)
REFERENCES_DIR = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..", "ml-service", "references")

MATCH_THESHOLD = 2.0
VOTE_FRACTION = 0.5

def _landmarksToTheVector(points):

    if not points:
     return [0.0] * 63

    points = np.array(points, dataType = np.float32)
    wrist = points[0]
    relative = points -wrist

    scaling = np.linalg.norm(points[9]-wrist)

   

    if scaling < 1e-6:
       scaling =1e-6
       relative = relative / scaling

    v1 = relative[9]
    v1_norm = np.linalg.norm(v1)

    if v1_norm < 1e-6:
         v1_norm = 1e-6
         vi = v1 /  v1_norm

    v2_raw = relative[5]
    v2 = v2_raw - np.dot(v2_raw, v1) * v1
    v2_norm = np.linalg.norm(v2)
    if v2_norm < 1e-6:
         v2_norm = 1e-6
         v2 = v2 / v2_norm
    v3 = np.cross(v1, v2)

    base=np.stack([v1, v2, v3])
    normalized = relative @ base.T
    return normalized.flatten().tolist()

    def _frameToTheFeature_vector(left_hand, right_hand):
       lefty = _landmarks_to_vector(left_hand)
       righty = _landmarks_to_vector(right_hand)
       return np.array(lefty + righty, dtype=np.float32)


    def _dtwDistance(seq_a, seq_b):
       n, m = len(seq_a), len(seq_b)
       cost = np.full((n + 1, m + 1), np.inf)
       cost[0, 0] = 0.0
       for i in range(1, n + 1):
               for j in range(1, m + 1):
                dist = np.linalg.norm(seq_a[i - 1] - seq_b[j - 1])
                cost[i, j] = dist + min(cost[i - 1, j], cost[i, j - 1], cost[i - 1, j - 1])
                return cost[n, m] / (n + m)






    def classify_sequence(landmark_sequence, sign_label):
        label_dir = os.path.join(REFERENCES_DIR, sign_label)
    if not os.path.isdir(label_dir):
        return "unknown", 0.0
 
    references = []
    for fname in sorted(os.listdir(label_dir)):
        if fname.endswith(".npy"):
            references.append(np.load(os.path.join(label_dir, fname)))
 
    if not references:
        return "unknown", 0.0
 
    sequence = [
        _frameToTheFeatureVector(frame.get("left_hand", []), frame.get("right_hand", []))
        for frame in landmark_sequence
    ]
    if len(sequence) < 3:
        return "unknown", 0.0
 
    distances = [_dtwDistance(sequence, ref) for ref in references]
    votes = sum(1 for d in distances if d <= MATCH_THRESHOLD)
    confidence = votes / len(distances)
 
    predicted_sign = sign_label if confidence >= VOTE_FRACTION else "unknown"
    return predicted_sign, confidence
 