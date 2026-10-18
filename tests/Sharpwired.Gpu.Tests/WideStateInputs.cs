// No Handwired source. The wide step of the phase 6 plan, task 7. It is a struct of 600 int fields that a
// loop with no maximum turns one place to the left in each iteration. The step program has one state cell for
// each field, so the step kernel has more than 512 cells. The translator refuses a local array as the
// state of a step program (a store in the step, and a choice between two arrays). So the 600 values are
// fields of a struct. A script wrote the fields and the statements of this file.

using Sharpwired.Annotations;

namespace Sharpwired.Gpu.Tests;

/// <summary>600 int values, C000 to C599.</summary>
public struct Wide600
{
    public int C000, C001, C002, C003, C004, C005, C006, C007, C008, C009;
    public int C010, C011, C012, C013, C014, C015, C016, C017, C018, C019;
    public int C020, C021, C022, C023, C024, C025, C026, C027, C028, C029;
    public int C030, C031, C032, C033, C034, C035, C036, C037, C038, C039;
    public int C040, C041, C042, C043, C044, C045, C046, C047, C048, C049;
    public int C050, C051, C052, C053, C054, C055, C056, C057, C058, C059;
    public int C060, C061, C062, C063, C064, C065, C066, C067, C068, C069;
    public int C070, C071, C072, C073, C074, C075, C076, C077, C078, C079;
    public int C080, C081, C082, C083, C084, C085, C086, C087, C088, C089;
    public int C090, C091, C092, C093, C094, C095, C096, C097, C098, C099;
    public int C100, C101, C102, C103, C104, C105, C106, C107, C108, C109;
    public int C110, C111, C112, C113, C114, C115, C116, C117, C118, C119;
    public int C120, C121, C122, C123, C124, C125, C126, C127, C128, C129;
    public int C130, C131, C132, C133, C134, C135, C136, C137, C138, C139;
    public int C140, C141, C142, C143, C144, C145, C146, C147, C148, C149;
    public int C150, C151, C152, C153, C154, C155, C156, C157, C158, C159;
    public int C160, C161, C162, C163, C164, C165, C166, C167, C168, C169;
    public int C170, C171, C172, C173, C174, C175, C176, C177, C178, C179;
    public int C180, C181, C182, C183, C184, C185, C186, C187, C188, C189;
    public int C190, C191, C192, C193, C194, C195, C196, C197, C198, C199;
    public int C200, C201, C202, C203, C204, C205, C206, C207, C208, C209;
    public int C210, C211, C212, C213, C214, C215, C216, C217, C218, C219;
    public int C220, C221, C222, C223, C224, C225, C226, C227, C228, C229;
    public int C230, C231, C232, C233, C234, C235, C236, C237, C238, C239;
    public int C240, C241, C242, C243, C244, C245, C246, C247, C248, C249;
    public int C250, C251, C252, C253, C254, C255, C256, C257, C258, C259;
    public int C260, C261, C262, C263, C264, C265, C266, C267, C268, C269;
    public int C270, C271, C272, C273, C274, C275, C276, C277, C278, C279;
    public int C280, C281, C282, C283, C284, C285, C286, C287, C288, C289;
    public int C290, C291, C292, C293, C294, C295, C296, C297, C298, C299;
    public int C300, C301, C302, C303, C304, C305, C306, C307, C308, C309;
    public int C310, C311, C312, C313, C314, C315, C316, C317, C318, C319;
    public int C320, C321, C322, C323, C324, C325, C326, C327, C328, C329;
    public int C330, C331, C332, C333, C334, C335, C336, C337, C338, C339;
    public int C340, C341, C342, C343, C344, C345, C346, C347, C348, C349;
    public int C350, C351, C352, C353, C354, C355, C356, C357, C358, C359;
    public int C360, C361, C362, C363, C364, C365, C366, C367, C368, C369;
    public int C370, C371, C372, C373, C374, C375, C376, C377, C378, C379;
    public int C380, C381, C382, C383, C384, C385, C386, C387, C388, C389;
    public int C390, C391, C392, C393, C394, C395, C396, C397, C398, C399;
    public int C400, C401, C402, C403, C404, C405, C406, C407, C408, C409;
    public int C410, C411, C412, C413, C414, C415, C416, C417, C418, C419;
    public int C420, C421, C422, C423, C424, C425, C426, C427, C428, C429;
    public int C430, C431, C432, C433, C434, C435, C436, C437, C438, C439;
    public int C440, C441, C442, C443, C444, C445, C446, C447, C448, C449;
    public int C450, C451, C452, C453, C454, C455, C456, C457, C458, C459;
    public int C460, C461, C462, C463, C464, C465, C466, C467, C468, C469;
    public int C470, C471, C472, C473, C474, C475, C476, C477, C478, C479;
    public int C480, C481, C482, C483, C484, C485, C486, C487, C488, C489;
    public int C490, C491, C492, C493, C494, C495, C496, C497, C498, C499;
    public int C500, C501, C502, C503, C504, C505, C506, C507, C508, C509;
    public int C510, C511, C512, C513, C514, C515, C516, C517, C518, C519;
    public int C520, C521, C522, C523, C524, C525, C526, C527, C528, C529;
    public int C530, C531, C532, C533, C534, C535, C536, C537, C538, C539;
    public int C540, C541, C542, C543, C544, C545, C546, C547, C548, C549;
    public int C550, C551, C552, C553, C554, C555, C556, C557, C558, C559;
    public int C560, C561, C562, C563, C564, C565, C566, C567, C568, C569;
    public int C570, C571, C572, C573, C574, C575, C576, C577, C578, C579;
    public int C580, C581, C582, C583, C584, C585, C586, C587, C588, C589;
    public int C590, C591, C592, C593, C594, C595, C596, C597, C598, C599;
}

public static class WideStateInputs
{
    // The loop of the plan: the struct turns n times. The ranges of the fields do not reach a fixed point
    // in 8 rounds, so each field gets the wide int range. The result reads two fields after the loop.
    [Handwired]
    public static int Rotate600([Range(0, 2000)] int n)
    {
        var cells = Start();
        int k = n;
        while (k > 0)
        {
            cells = TurnLeft(cells);
            k--;
        }

        return cells.C000 + cells.C599;
    }

    // Field i holds i.
    [Handwired]
    public static Wide600 Start()
    {
        var cells = default(Wide600);
        cells.C000 = 0; cells.C001 = 1; cells.C002 = 2; cells.C003 = 3; cells.C004 = 4; cells.C005 = 5;
        cells.C006 = 6; cells.C007 = 7; cells.C008 = 8; cells.C009 = 9; cells.C010 = 10; cells.C011 = 11;
        cells.C012 = 12; cells.C013 = 13; cells.C014 = 14; cells.C015 = 15; cells.C016 = 16; cells.C017 = 17;
        cells.C018 = 18; cells.C019 = 19; cells.C020 = 20; cells.C021 = 21; cells.C022 = 22; cells.C023 = 23;
        cells.C024 = 24; cells.C025 = 25; cells.C026 = 26; cells.C027 = 27; cells.C028 = 28; cells.C029 = 29;
        cells.C030 = 30; cells.C031 = 31; cells.C032 = 32; cells.C033 = 33; cells.C034 = 34; cells.C035 = 35;
        cells.C036 = 36; cells.C037 = 37; cells.C038 = 38; cells.C039 = 39; cells.C040 = 40; cells.C041 = 41;
        cells.C042 = 42; cells.C043 = 43; cells.C044 = 44; cells.C045 = 45; cells.C046 = 46; cells.C047 = 47;
        cells.C048 = 48; cells.C049 = 49; cells.C050 = 50; cells.C051 = 51; cells.C052 = 52; cells.C053 = 53;
        cells.C054 = 54; cells.C055 = 55; cells.C056 = 56; cells.C057 = 57; cells.C058 = 58; cells.C059 = 59;
        cells.C060 = 60; cells.C061 = 61; cells.C062 = 62; cells.C063 = 63; cells.C064 = 64; cells.C065 = 65;
        cells.C066 = 66; cells.C067 = 67; cells.C068 = 68; cells.C069 = 69; cells.C070 = 70; cells.C071 = 71;
        cells.C072 = 72; cells.C073 = 73; cells.C074 = 74; cells.C075 = 75; cells.C076 = 76; cells.C077 = 77;
        cells.C078 = 78; cells.C079 = 79; cells.C080 = 80; cells.C081 = 81; cells.C082 = 82; cells.C083 = 83;
        cells.C084 = 84; cells.C085 = 85; cells.C086 = 86; cells.C087 = 87; cells.C088 = 88; cells.C089 = 89;
        cells.C090 = 90; cells.C091 = 91; cells.C092 = 92; cells.C093 = 93; cells.C094 = 94; cells.C095 = 95;
        cells.C096 = 96; cells.C097 = 97; cells.C098 = 98; cells.C099 = 99; cells.C100 = 100; cells.C101 = 101;
        cells.C102 = 102; cells.C103 = 103; cells.C104 = 104; cells.C105 = 105; cells.C106 = 106; cells.C107 = 107;
        cells.C108 = 108; cells.C109 = 109; cells.C110 = 110; cells.C111 = 111; cells.C112 = 112; cells.C113 = 113;
        cells.C114 = 114; cells.C115 = 115; cells.C116 = 116; cells.C117 = 117; cells.C118 = 118; cells.C119 = 119;
        cells.C120 = 120; cells.C121 = 121; cells.C122 = 122; cells.C123 = 123; cells.C124 = 124; cells.C125 = 125;
        cells.C126 = 126; cells.C127 = 127; cells.C128 = 128; cells.C129 = 129; cells.C130 = 130; cells.C131 = 131;
        cells.C132 = 132; cells.C133 = 133; cells.C134 = 134; cells.C135 = 135; cells.C136 = 136; cells.C137 = 137;
        cells.C138 = 138; cells.C139 = 139; cells.C140 = 140; cells.C141 = 141; cells.C142 = 142; cells.C143 = 143;
        cells.C144 = 144; cells.C145 = 145; cells.C146 = 146; cells.C147 = 147; cells.C148 = 148; cells.C149 = 149;
        cells.C150 = 150; cells.C151 = 151; cells.C152 = 152; cells.C153 = 153; cells.C154 = 154; cells.C155 = 155;
        cells.C156 = 156; cells.C157 = 157; cells.C158 = 158; cells.C159 = 159; cells.C160 = 160; cells.C161 = 161;
        cells.C162 = 162; cells.C163 = 163; cells.C164 = 164; cells.C165 = 165; cells.C166 = 166; cells.C167 = 167;
        cells.C168 = 168; cells.C169 = 169; cells.C170 = 170; cells.C171 = 171; cells.C172 = 172; cells.C173 = 173;
        cells.C174 = 174; cells.C175 = 175; cells.C176 = 176; cells.C177 = 177; cells.C178 = 178; cells.C179 = 179;
        cells.C180 = 180; cells.C181 = 181; cells.C182 = 182; cells.C183 = 183; cells.C184 = 184; cells.C185 = 185;
        cells.C186 = 186; cells.C187 = 187; cells.C188 = 188; cells.C189 = 189; cells.C190 = 190; cells.C191 = 191;
        cells.C192 = 192; cells.C193 = 193; cells.C194 = 194; cells.C195 = 195; cells.C196 = 196; cells.C197 = 197;
        cells.C198 = 198; cells.C199 = 199; cells.C200 = 200; cells.C201 = 201; cells.C202 = 202; cells.C203 = 203;
        cells.C204 = 204; cells.C205 = 205; cells.C206 = 206; cells.C207 = 207; cells.C208 = 208; cells.C209 = 209;
        cells.C210 = 210; cells.C211 = 211; cells.C212 = 212; cells.C213 = 213; cells.C214 = 214; cells.C215 = 215;
        cells.C216 = 216; cells.C217 = 217; cells.C218 = 218; cells.C219 = 219; cells.C220 = 220; cells.C221 = 221;
        cells.C222 = 222; cells.C223 = 223; cells.C224 = 224; cells.C225 = 225; cells.C226 = 226; cells.C227 = 227;
        cells.C228 = 228; cells.C229 = 229; cells.C230 = 230; cells.C231 = 231; cells.C232 = 232; cells.C233 = 233;
        cells.C234 = 234; cells.C235 = 235; cells.C236 = 236; cells.C237 = 237; cells.C238 = 238; cells.C239 = 239;
        cells.C240 = 240; cells.C241 = 241; cells.C242 = 242; cells.C243 = 243; cells.C244 = 244; cells.C245 = 245;
        cells.C246 = 246; cells.C247 = 247; cells.C248 = 248; cells.C249 = 249; cells.C250 = 250; cells.C251 = 251;
        cells.C252 = 252; cells.C253 = 253; cells.C254 = 254; cells.C255 = 255; cells.C256 = 256; cells.C257 = 257;
        cells.C258 = 258; cells.C259 = 259; cells.C260 = 260; cells.C261 = 261; cells.C262 = 262; cells.C263 = 263;
        cells.C264 = 264; cells.C265 = 265; cells.C266 = 266; cells.C267 = 267; cells.C268 = 268; cells.C269 = 269;
        cells.C270 = 270; cells.C271 = 271; cells.C272 = 272; cells.C273 = 273; cells.C274 = 274; cells.C275 = 275;
        cells.C276 = 276; cells.C277 = 277; cells.C278 = 278; cells.C279 = 279; cells.C280 = 280; cells.C281 = 281;
        cells.C282 = 282; cells.C283 = 283; cells.C284 = 284; cells.C285 = 285; cells.C286 = 286; cells.C287 = 287;
        cells.C288 = 288; cells.C289 = 289; cells.C290 = 290; cells.C291 = 291; cells.C292 = 292; cells.C293 = 293;
        cells.C294 = 294; cells.C295 = 295; cells.C296 = 296; cells.C297 = 297; cells.C298 = 298; cells.C299 = 299;
        cells.C300 = 300; cells.C301 = 301; cells.C302 = 302; cells.C303 = 303; cells.C304 = 304; cells.C305 = 305;
        cells.C306 = 306; cells.C307 = 307; cells.C308 = 308; cells.C309 = 309; cells.C310 = 310; cells.C311 = 311;
        cells.C312 = 312; cells.C313 = 313; cells.C314 = 314; cells.C315 = 315; cells.C316 = 316; cells.C317 = 317;
        cells.C318 = 318; cells.C319 = 319; cells.C320 = 320; cells.C321 = 321; cells.C322 = 322; cells.C323 = 323;
        cells.C324 = 324; cells.C325 = 325; cells.C326 = 326; cells.C327 = 327; cells.C328 = 328; cells.C329 = 329;
        cells.C330 = 330; cells.C331 = 331; cells.C332 = 332; cells.C333 = 333; cells.C334 = 334; cells.C335 = 335;
        cells.C336 = 336; cells.C337 = 337; cells.C338 = 338; cells.C339 = 339; cells.C340 = 340; cells.C341 = 341;
        cells.C342 = 342; cells.C343 = 343; cells.C344 = 344; cells.C345 = 345; cells.C346 = 346; cells.C347 = 347;
        cells.C348 = 348; cells.C349 = 349; cells.C350 = 350; cells.C351 = 351; cells.C352 = 352; cells.C353 = 353;
        cells.C354 = 354; cells.C355 = 355; cells.C356 = 356; cells.C357 = 357; cells.C358 = 358; cells.C359 = 359;
        cells.C360 = 360; cells.C361 = 361; cells.C362 = 362; cells.C363 = 363; cells.C364 = 364; cells.C365 = 365;
        cells.C366 = 366; cells.C367 = 367; cells.C368 = 368; cells.C369 = 369; cells.C370 = 370; cells.C371 = 371;
        cells.C372 = 372; cells.C373 = 373; cells.C374 = 374; cells.C375 = 375; cells.C376 = 376; cells.C377 = 377;
        cells.C378 = 378; cells.C379 = 379; cells.C380 = 380; cells.C381 = 381; cells.C382 = 382; cells.C383 = 383;
        cells.C384 = 384; cells.C385 = 385; cells.C386 = 386; cells.C387 = 387; cells.C388 = 388; cells.C389 = 389;
        cells.C390 = 390; cells.C391 = 391; cells.C392 = 392; cells.C393 = 393; cells.C394 = 394; cells.C395 = 395;
        cells.C396 = 396; cells.C397 = 397; cells.C398 = 398; cells.C399 = 399; cells.C400 = 400; cells.C401 = 401;
        cells.C402 = 402; cells.C403 = 403; cells.C404 = 404; cells.C405 = 405; cells.C406 = 406; cells.C407 = 407;
        cells.C408 = 408; cells.C409 = 409; cells.C410 = 410; cells.C411 = 411; cells.C412 = 412; cells.C413 = 413;
        cells.C414 = 414; cells.C415 = 415; cells.C416 = 416; cells.C417 = 417; cells.C418 = 418; cells.C419 = 419;
        cells.C420 = 420; cells.C421 = 421; cells.C422 = 422; cells.C423 = 423; cells.C424 = 424; cells.C425 = 425;
        cells.C426 = 426; cells.C427 = 427; cells.C428 = 428; cells.C429 = 429; cells.C430 = 430; cells.C431 = 431;
        cells.C432 = 432; cells.C433 = 433; cells.C434 = 434; cells.C435 = 435; cells.C436 = 436; cells.C437 = 437;
        cells.C438 = 438; cells.C439 = 439; cells.C440 = 440; cells.C441 = 441; cells.C442 = 442; cells.C443 = 443;
        cells.C444 = 444; cells.C445 = 445; cells.C446 = 446; cells.C447 = 447; cells.C448 = 448; cells.C449 = 449;
        cells.C450 = 450; cells.C451 = 451; cells.C452 = 452; cells.C453 = 453; cells.C454 = 454; cells.C455 = 455;
        cells.C456 = 456; cells.C457 = 457; cells.C458 = 458; cells.C459 = 459; cells.C460 = 460; cells.C461 = 461;
        cells.C462 = 462; cells.C463 = 463; cells.C464 = 464; cells.C465 = 465; cells.C466 = 466; cells.C467 = 467;
        cells.C468 = 468; cells.C469 = 469; cells.C470 = 470; cells.C471 = 471; cells.C472 = 472; cells.C473 = 473;
        cells.C474 = 474; cells.C475 = 475; cells.C476 = 476; cells.C477 = 477; cells.C478 = 478; cells.C479 = 479;
        cells.C480 = 480; cells.C481 = 481; cells.C482 = 482; cells.C483 = 483; cells.C484 = 484; cells.C485 = 485;
        cells.C486 = 486; cells.C487 = 487; cells.C488 = 488; cells.C489 = 489; cells.C490 = 490; cells.C491 = 491;
        cells.C492 = 492; cells.C493 = 493; cells.C494 = 494; cells.C495 = 495; cells.C496 = 496; cells.C497 = 497;
        cells.C498 = 498; cells.C499 = 499; cells.C500 = 500; cells.C501 = 501; cells.C502 = 502; cells.C503 = 503;
        cells.C504 = 504; cells.C505 = 505; cells.C506 = 506; cells.C507 = 507; cells.C508 = 508; cells.C509 = 509;
        cells.C510 = 510; cells.C511 = 511; cells.C512 = 512; cells.C513 = 513; cells.C514 = 514; cells.C515 = 515;
        cells.C516 = 516; cells.C517 = 517; cells.C518 = 518; cells.C519 = 519; cells.C520 = 520; cells.C521 = 521;
        cells.C522 = 522; cells.C523 = 523; cells.C524 = 524; cells.C525 = 525; cells.C526 = 526; cells.C527 = 527;
        cells.C528 = 528; cells.C529 = 529; cells.C530 = 530; cells.C531 = 531; cells.C532 = 532; cells.C533 = 533;
        cells.C534 = 534; cells.C535 = 535; cells.C536 = 536; cells.C537 = 537; cells.C538 = 538; cells.C539 = 539;
        cells.C540 = 540; cells.C541 = 541; cells.C542 = 542; cells.C543 = 543; cells.C544 = 544; cells.C545 = 545;
        cells.C546 = 546; cells.C547 = 547; cells.C548 = 548; cells.C549 = 549; cells.C550 = 550; cells.C551 = 551;
        cells.C552 = 552; cells.C553 = 553; cells.C554 = 554; cells.C555 = 555; cells.C556 = 556; cells.C557 = 557;
        cells.C558 = 558; cells.C559 = 559; cells.C560 = 560; cells.C561 = 561; cells.C562 = 562; cells.C563 = 563;
        cells.C564 = 564; cells.C565 = 565; cells.C566 = 566; cells.C567 = 567; cells.C568 = 568; cells.C569 = 569;
        cells.C570 = 570; cells.C571 = 571; cells.C572 = 572; cells.C573 = 573; cells.C574 = 574; cells.C575 = 575;
        cells.C576 = 576; cells.C577 = 577; cells.C578 = 578; cells.C579 = 579; cells.C580 = 580; cells.C581 = 581;
        cells.C582 = 582; cells.C583 = 583; cells.C584 = 584; cells.C585 = 585; cells.C586 = 586; cells.C587 = 587;
        cells.C588 = 588; cells.C589 = 589; cells.C590 = 590; cells.C591 = 591; cells.C592 = 592; cells.C593 = 593;
        cells.C594 = 594; cells.C595 = 595; cells.C596 = 596; cells.C597 = 597; cells.C598 = 598; cells.C599 = 599;
        return cells;
    }

    // Field i gets field i + 1, and the last field gets field 0.
    [Handwired]
    public static Wide600 TurnLeft(Wide600 cells)
    {
        var next = cells;
        next.C000 = cells.C001; next.C001 = cells.C002; next.C002 = cells.C003; next.C003 = cells.C004;
        next.C004 = cells.C005; next.C005 = cells.C006; next.C006 = cells.C007; next.C007 = cells.C008;
        next.C008 = cells.C009; next.C009 = cells.C010; next.C010 = cells.C011; next.C011 = cells.C012;
        next.C012 = cells.C013; next.C013 = cells.C014; next.C014 = cells.C015; next.C015 = cells.C016;
        next.C016 = cells.C017; next.C017 = cells.C018; next.C018 = cells.C019; next.C019 = cells.C020;
        next.C020 = cells.C021; next.C021 = cells.C022; next.C022 = cells.C023; next.C023 = cells.C024;
        next.C024 = cells.C025; next.C025 = cells.C026; next.C026 = cells.C027; next.C027 = cells.C028;
        next.C028 = cells.C029; next.C029 = cells.C030; next.C030 = cells.C031; next.C031 = cells.C032;
        next.C032 = cells.C033; next.C033 = cells.C034; next.C034 = cells.C035; next.C035 = cells.C036;
        next.C036 = cells.C037; next.C037 = cells.C038; next.C038 = cells.C039; next.C039 = cells.C040;
        next.C040 = cells.C041; next.C041 = cells.C042; next.C042 = cells.C043; next.C043 = cells.C044;
        next.C044 = cells.C045; next.C045 = cells.C046; next.C046 = cells.C047; next.C047 = cells.C048;
        next.C048 = cells.C049; next.C049 = cells.C050; next.C050 = cells.C051; next.C051 = cells.C052;
        next.C052 = cells.C053; next.C053 = cells.C054; next.C054 = cells.C055; next.C055 = cells.C056;
        next.C056 = cells.C057; next.C057 = cells.C058; next.C058 = cells.C059; next.C059 = cells.C060;
        next.C060 = cells.C061; next.C061 = cells.C062; next.C062 = cells.C063; next.C063 = cells.C064;
        next.C064 = cells.C065; next.C065 = cells.C066; next.C066 = cells.C067; next.C067 = cells.C068;
        next.C068 = cells.C069; next.C069 = cells.C070; next.C070 = cells.C071; next.C071 = cells.C072;
        next.C072 = cells.C073; next.C073 = cells.C074; next.C074 = cells.C075; next.C075 = cells.C076;
        next.C076 = cells.C077; next.C077 = cells.C078; next.C078 = cells.C079; next.C079 = cells.C080;
        next.C080 = cells.C081; next.C081 = cells.C082; next.C082 = cells.C083; next.C083 = cells.C084;
        next.C084 = cells.C085; next.C085 = cells.C086; next.C086 = cells.C087; next.C087 = cells.C088;
        next.C088 = cells.C089; next.C089 = cells.C090; next.C090 = cells.C091; next.C091 = cells.C092;
        next.C092 = cells.C093; next.C093 = cells.C094; next.C094 = cells.C095; next.C095 = cells.C096;
        next.C096 = cells.C097; next.C097 = cells.C098; next.C098 = cells.C099; next.C099 = cells.C100;
        next.C100 = cells.C101; next.C101 = cells.C102; next.C102 = cells.C103; next.C103 = cells.C104;
        next.C104 = cells.C105; next.C105 = cells.C106; next.C106 = cells.C107; next.C107 = cells.C108;
        next.C108 = cells.C109; next.C109 = cells.C110; next.C110 = cells.C111; next.C111 = cells.C112;
        next.C112 = cells.C113; next.C113 = cells.C114; next.C114 = cells.C115; next.C115 = cells.C116;
        next.C116 = cells.C117; next.C117 = cells.C118; next.C118 = cells.C119; next.C119 = cells.C120;
        next.C120 = cells.C121; next.C121 = cells.C122; next.C122 = cells.C123; next.C123 = cells.C124;
        next.C124 = cells.C125; next.C125 = cells.C126; next.C126 = cells.C127; next.C127 = cells.C128;
        next.C128 = cells.C129; next.C129 = cells.C130; next.C130 = cells.C131; next.C131 = cells.C132;
        next.C132 = cells.C133; next.C133 = cells.C134; next.C134 = cells.C135; next.C135 = cells.C136;
        next.C136 = cells.C137; next.C137 = cells.C138; next.C138 = cells.C139; next.C139 = cells.C140;
        next.C140 = cells.C141; next.C141 = cells.C142; next.C142 = cells.C143; next.C143 = cells.C144;
        next.C144 = cells.C145; next.C145 = cells.C146; next.C146 = cells.C147; next.C147 = cells.C148;
        next.C148 = cells.C149; next.C149 = cells.C150; next.C150 = cells.C151; next.C151 = cells.C152;
        next.C152 = cells.C153; next.C153 = cells.C154; next.C154 = cells.C155; next.C155 = cells.C156;
        next.C156 = cells.C157; next.C157 = cells.C158; next.C158 = cells.C159; next.C159 = cells.C160;
        next.C160 = cells.C161; next.C161 = cells.C162; next.C162 = cells.C163; next.C163 = cells.C164;
        next.C164 = cells.C165; next.C165 = cells.C166; next.C166 = cells.C167; next.C167 = cells.C168;
        next.C168 = cells.C169; next.C169 = cells.C170; next.C170 = cells.C171; next.C171 = cells.C172;
        next.C172 = cells.C173; next.C173 = cells.C174; next.C174 = cells.C175; next.C175 = cells.C176;
        next.C176 = cells.C177; next.C177 = cells.C178; next.C178 = cells.C179; next.C179 = cells.C180;
        next.C180 = cells.C181; next.C181 = cells.C182; next.C182 = cells.C183; next.C183 = cells.C184;
        next.C184 = cells.C185; next.C185 = cells.C186; next.C186 = cells.C187; next.C187 = cells.C188;
        next.C188 = cells.C189; next.C189 = cells.C190; next.C190 = cells.C191; next.C191 = cells.C192;
        next.C192 = cells.C193; next.C193 = cells.C194; next.C194 = cells.C195; next.C195 = cells.C196;
        next.C196 = cells.C197; next.C197 = cells.C198; next.C198 = cells.C199; next.C199 = cells.C200;
        next.C200 = cells.C201; next.C201 = cells.C202; next.C202 = cells.C203; next.C203 = cells.C204;
        next.C204 = cells.C205; next.C205 = cells.C206; next.C206 = cells.C207; next.C207 = cells.C208;
        next.C208 = cells.C209; next.C209 = cells.C210; next.C210 = cells.C211; next.C211 = cells.C212;
        next.C212 = cells.C213; next.C213 = cells.C214; next.C214 = cells.C215; next.C215 = cells.C216;
        next.C216 = cells.C217; next.C217 = cells.C218; next.C218 = cells.C219; next.C219 = cells.C220;
        next.C220 = cells.C221; next.C221 = cells.C222; next.C222 = cells.C223; next.C223 = cells.C224;
        next.C224 = cells.C225; next.C225 = cells.C226; next.C226 = cells.C227; next.C227 = cells.C228;
        next.C228 = cells.C229; next.C229 = cells.C230; next.C230 = cells.C231; next.C231 = cells.C232;
        next.C232 = cells.C233; next.C233 = cells.C234; next.C234 = cells.C235; next.C235 = cells.C236;
        next.C236 = cells.C237; next.C237 = cells.C238; next.C238 = cells.C239; next.C239 = cells.C240;
        next.C240 = cells.C241; next.C241 = cells.C242; next.C242 = cells.C243; next.C243 = cells.C244;
        next.C244 = cells.C245; next.C245 = cells.C246; next.C246 = cells.C247; next.C247 = cells.C248;
        next.C248 = cells.C249; next.C249 = cells.C250; next.C250 = cells.C251; next.C251 = cells.C252;
        next.C252 = cells.C253; next.C253 = cells.C254; next.C254 = cells.C255; next.C255 = cells.C256;
        next.C256 = cells.C257; next.C257 = cells.C258; next.C258 = cells.C259; next.C259 = cells.C260;
        next.C260 = cells.C261; next.C261 = cells.C262; next.C262 = cells.C263; next.C263 = cells.C264;
        next.C264 = cells.C265; next.C265 = cells.C266; next.C266 = cells.C267; next.C267 = cells.C268;
        next.C268 = cells.C269; next.C269 = cells.C270; next.C270 = cells.C271; next.C271 = cells.C272;
        next.C272 = cells.C273; next.C273 = cells.C274; next.C274 = cells.C275; next.C275 = cells.C276;
        next.C276 = cells.C277; next.C277 = cells.C278; next.C278 = cells.C279; next.C279 = cells.C280;
        next.C280 = cells.C281; next.C281 = cells.C282; next.C282 = cells.C283; next.C283 = cells.C284;
        next.C284 = cells.C285; next.C285 = cells.C286; next.C286 = cells.C287; next.C287 = cells.C288;
        next.C288 = cells.C289; next.C289 = cells.C290; next.C290 = cells.C291; next.C291 = cells.C292;
        next.C292 = cells.C293; next.C293 = cells.C294; next.C294 = cells.C295; next.C295 = cells.C296;
        next.C296 = cells.C297; next.C297 = cells.C298; next.C298 = cells.C299; next.C299 = cells.C300;
        next.C300 = cells.C301; next.C301 = cells.C302; next.C302 = cells.C303; next.C303 = cells.C304;
        next.C304 = cells.C305; next.C305 = cells.C306; next.C306 = cells.C307; next.C307 = cells.C308;
        next.C308 = cells.C309; next.C309 = cells.C310; next.C310 = cells.C311; next.C311 = cells.C312;
        next.C312 = cells.C313; next.C313 = cells.C314; next.C314 = cells.C315; next.C315 = cells.C316;
        next.C316 = cells.C317; next.C317 = cells.C318; next.C318 = cells.C319; next.C319 = cells.C320;
        next.C320 = cells.C321; next.C321 = cells.C322; next.C322 = cells.C323; next.C323 = cells.C324;
        next.C324 = cells.C325; next.C325 = cells.C326; next.C326 = cells.C327; next.C327 = cells.C328;
        next.C328 = cells.C329; next.C329 = cells.C330; next.C330 = cells.C331; next.C331 = cells.C332;
        next.C332 = cells.C333; next.C333 = cells.C334; next.C334 = cells.C335; next.C335 = cells.C336;
        next.C336 = cells.C337; next.C337 = cells.C338; next.C338 = cells.C339; next.C339 = cells.C340;
        next.C340 = cells.C341; next.C341 = cells.C342; next.C342 = cells.C343; next.C343 = cells.C344;
        next.C344 = cells.C345; next.C345 = cells.C346; next.C346 = cells.C347; next.C347 = cells.C348;
        next.C348 = cells.C349; next.C349 = cells.C350; next.C350 = cells.C351; next.C351 = cells.C352;
        next.C352 = cells.C353; next.C353 = cells.C354; next.C354 = cells.C355; next.C355 = cells.C356;
        next.C356 = cells.C357; next.C357 = cells.C358; next.C358 = cells.C359; next.C359 = cells.C360;
        next.C360 = cells.C361; next.C361 = cells.C362; next.C362 = cells.C363; next.C363 = cells.C364;
        next.C364 = cells.C365; next.C365 = cells.C366; next.C366 = cells.C367; next.C367 = cells.C368;
        next.C368 = cells.C369; next.C369 = cells.C370; next.C370 = cells.C371; next.C371 = cells.C372;
        next.C372 = cells.C373; next.C373 = cells.C374; next.C374 = cells.C375; next.C375 = cells.C376;
        next.C376 = cells.C377; next.C377 = cells.C378; next.C378 = cells.C379; next.C379 = cells.C380;
        next.C380 = cells.C381; next.C381 = cells.C382; next.C382 = cells.C383; next.C383 = cells.C384;
        next.C384 = cells.C385; next.C385 = cells.C386; next.C386 = cells.C387; next.C387 = cells.C388;
        next.C388 = cells.C389; next.C389 = cells.C390; next.C390 = cells.C391; next.C391 = cells.C392;
        next.C392 = cells.C393; next.C393 = cells.C394; next.C394 = cells.C395; next.C395 = cells.C396;
        next.C396 = cells.C397; next.C397 = cells.C398; next.C398 = cells.C399; next.C399 = cells.C400;
        next.C400 = cells.C401; next.C401 = cells.C402; next.C402 = cells.C403; next.C403 = cells.C404;
        next.C404 = cells.C405; next.C405 = cells.C406; next.C406 = cells.C407; next.C407 = cells.C408;
        next.C408 = cells.C409; next.C409 = cells.C410; next.C410 = cells.C411; next.C411 = cells.C412;
        next.C412 = cells.C413; next.C413 = cells.C414; next.C414 = cells.C415; next.C415 = cells.C416;
        next.C416 = cells.C417; next.C417 = cells.C418; next.C418 = cells.C419; next.C419 = cells.C420;
        next.C420 = cells.C421; next.C421 = cells.C422; next.C422 = cells.C423; next.C423 = cells.C424;
        next.C424 = cells.C425; next.C425 = cells.C426; next.C426 = cells.C427; next.C427 = cells.C428;
        next.C428 = cells.C429; next.C429 = cells.C430; next.C430 = cells.C431; next.C431 = cells.C432;
        next.C432 = cells.C433; next.C433 = cells.C434; next.C434 = cells.C435; next.C435 = cells.C436;
        next.C436 = cells.C437; next.C437 = cells.C438; next.C438 = cells.C439; next.C439 = cells.C440;
        next.C440 = cells.C441; next.C441 = cells.C442; next.C442 = cells.C443; next.C443 = cells.C444;
        next.C444 = cells.C445; next.C445 = cells.C446; next.C446 = cells.C447; next.C447 = cells.C448;
        next.C448 = cells.C449; next.C449 = cells.C450; next.C450 = cells.C451; next.C451 = cells.C452;
        next.C452 = cells.C453; next.C453 = cells.C454; next.C454 = cells.C455; next.C455 = cells.C456;
        next.C456 = cells.C457; next.C457 = cells.C458; next.C458 = cells.C459; next.C459 = cells.C460;
        next.C460 = cells.C461; next.C461 = cells.C462; next.C462 = cells.C463; next.C463 = cells.C464;
        next.C464 = cells.C465; next.C465 = cells.C466; next.C466 = cells.C467; next.C467 = cells.C468;
        next.C468 = cells.C469; next.C469 = cells.C470; next.C470 = cells.C471; next.C471 = cells.C472;
        next.C472 = cells.C473; next.C473 = cells.C474; next.C474 = cells.C475; next.C475 = cells.C476;
        next.C476 = cells.C477; next.C477 = cells.C478; next.C478 = cells.C479; next.C479 = cells.C480;
        next.C480 = cells.C481; next.C481 = cells.C482; next.C482 = cells.C483; next.C483 = cells.C484;
        next.C484 = cells.C485; next.C485 = cells.C486; next.C486 = cells.C487; next.C487 = cells.C488;
        next.C488 = cells.C489; next.C489 = cells.C490; next.C490 = cells.C491; next.C491 = cells.C492;
        next.C492 = cells.C493; next.C493 = cells.C494; next.C494 = cells.C495; next.C495 = cells.C496;
        next.C496 = cells.C497; next.C497 = cells.C498; next.C498 = cells.C499; next.C499 = cells.C500;
        next.C500 = cells.C501; next.C501 = cells.C502; next.C502 = cells.C503; next.C503 = cells.C504;
        next.C504 = cells.C505; next.C505 = cells.C506; next.C506 = cells.C507; next.C507 = cells.C508;
        next.C508 = cells.C509; next.C509 = cells.C510; next.C510 = cells.C511; next.C511 = cells.C512;
        next.C512 = cells.C513; next.C513 = cells.C514; next.C514 = cells.C515; next.C515 = cells.C516;
        next.C516 = cells.C517; next.C517 = cells.C518; next.C518 = cells.C519; next.C519 = cells.C520;
        next.C520 = cells.C521; next.C521 = cells.C522; next.C522 = cells.C523; next.C523 = cells.C524;
        next.C524 = cells.C525; next.C525 = cells.C526; next.C526 = cells.C527; next.C527 = cells.C528;
        next.C528 = cells.C529; next.C529 = cells.C530; next.C530 = cells.C531; next.C531 = cells.C532;
        next.C532 = cells.C533; next.C533 = cells.C534; next.C534 = cells.C535; next.C535 = cells.C536;
        next.C536 = cells.C537; next.C537 = cells.C538; next.C538 = cells.C539; next.C539 = cells.C540;
        next.C540 = cells.C541; next.C541 = cells.C542; next.C542 = cells.C543; next.C543 = cells.C544;
        next.C544 = cells.C545; next.C545 = cells.C546; next.C546 = cells.C547; next.C547 = cells.C548;
        next.C548 = cells.C549; next.C549 = cells.C550; next.C550 = cells.C551; next.C551 = cells.C552;
        next.C552 = cells.C553; next.C553 = cells.C554; next.C554 = cells.C555; next.C555 = cells.C556;
        next.C556 = cells.C557; next.C557 = cells.C558; next.C558 = cells.C559; next.C559 = cells.C560;
        next.C560 = cells.C561; next.C561 = cells.C562; next.C562 = cells.C563; next.C563 = cells.C564;
        next.C564 = cells.C565; next.C565 = cells.C566; next.C566 = cells.C567; next.C567 = cells.C568;
        next.C568 = cells.C569; next.C569 = cells.C570; next.C570 = cells.C571; next.C571 = cells.C572;
        next.C572 = cells.C573; next.C573 = cells.C574; next.C574 = cells.C575; next.C575 = cells.C576;
        next.C576 = cells.C577; next.C577 = cells.C578; next.C578 = cells.C579; next.C579 = cells.C580;
        next.C580 = cells.C581; next.C581 = cells.C582; next.C582 = cells.C583; next.C583 = cells.C584;
        next.C584 = cells.C585; next.C585 = cells.C586; next.C586 = cells.C587; next.C587 = cells.C588;
        next.C588 = cells.C589; next.C589 = cells.C590; next.C590 = cells.C591; next.C591 = cells.C592;
        next.C592 = cells.C593; next.C593 = cells.C594; next.C594 = cells.C595; next.C595 = cells.C596;
        next.C596 = cells.C597; next.C597 = cells.C598; next.C598 = cells.C599; next.C599 = cells.C000;
        return next;
    }
}
