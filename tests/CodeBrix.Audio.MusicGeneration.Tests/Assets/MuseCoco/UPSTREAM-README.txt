MUSECOCO SYNTHETIC OFFLINE FIXTURES
=================================
The music and text ONNX graphs are tiny synthetic weights authored for these
tests. They do not contain checkpoint weights. Schema/vocabulary data is derived
from Microsoft Muzic's MuseCoco (MIT); WordPiece vocabulary and algorithm follow
BERT/transformers (Apache-2.0). See THIRD-PARTY-NOTICES entries 18 and 20.

Run generate_fixtures.py manually with --text-bundle pointing to a staged
MuseCoco BERT bundle and --bert-source pointing to the publisher tokenizer files.
It requires numpy, onnx and transformers. Checked-in expected tokens come from
the publisher tokenizer; tests themselves run without Python or ONNX Runtime.
The synthetic decoder exercises recurrent state, positional prefix handling,
EOS, sampling, note cleanup, piano/percussion and metadata. The BERT graph
exercises the complete driver contract and all 60 label mappings. Real-model
staging, all precisions and mixed precision are covered by MuseCocoLiveTests in
the separate gated EndToEnd project. Real-checkpoint numerical and MIDI evidence
is summarized in MAINTAINER-README.txt.
